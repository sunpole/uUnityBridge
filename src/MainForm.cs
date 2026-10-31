using System.Drawing;
using System.Text;

namespace uUnityBridge;

internal sealed class MainForm : Form
{
    private readonly Button receive = BigButton("ИЗ ЧАТА", Color.FromArgb(35, 75, 132));
    private readonly Button send = BigButton("ПЕРЕДАТЬ ЧАТУ", Color.FromArgb(31, 111, 81));
    private readonly Button copy = BigButton("СКОПИРОВАТЬ", Color.FromArgb(81, 62, 127));
    private readonly ListBox patches = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11), IntegralHeight = false };
    private readonly TextBox details = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true,
        ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10),
        BackColor = Color.FromArgb(23, 26, 34), ForeColor = Color.White, BorderStyle = BorderStyle.None };
    private readonly Label status = new() { Text = "Готово · sunpole/uUnityBridge", Dock = DockStyle.Fill,
        Padding = new Padding(10), AutoEllipsis = true, Font = new Font("Segoe UI", 10) };

    private static Button BigButton(string text, Color color) => new()
    {
        Text = text, BackColor = color, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
        Font = new Font("Segoe UI Semibold", 12, FontStyle.Bold), Height = 65,
        Dock = DockStyle.Fill, Cursor = Cursors.Hand, TabStop = true
    };

    internal MainForm()
    {
        Text = "uUnity Bridge · PATCH / ANS · 0.2.0";
        MinimumSize = new Size(740, 480);
        Size = new Size(940, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(241, 244, 249);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 4, ColumnCount = 1 };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        grid.Controls.Add(new Label { Text = "uUnity Bridge  ·  PATCH / ANS", Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 18, FontStyle.Bold) }, 0, 0);
        var buttons = new TableLayoutPanel { ColumnCount = 3, Dock = DockStyle.Fill };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        buttons.Controls.Add(receive, 0, 0);
        buttons.Controls.Add(send, 1, 0);
        buttons.Controls.Add(copy, 2, 0);
        grid.Controls.Add(buttons, 0, 1);
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(patches);
        split.Panel2.Controls.Add(details);
        grid.Controls.Add(split, 0, 2);
        grid.Controls.Add(status, 0, 3);
        Controls.Add(grid);
        Shown += (_, _) => { if (split.Width > 450) split.SplitterDistance = 310; };

        patches.SelectedIndexChanged += (_, _) => DescribeSelection();
        receive.Click += async (_, _) => await ExecuteUiAsync(ReceiveAndApplyAsync);
        send.Click += async (_, _) => await ExecuteUiAsync(PublishAnswersAsync);
        copy.Click += (_, _) => CopyNotification();

        details.Text = "ИЗ ЧАТА — получить и автоматически обработать все новые разрешённые патчи.\r\n\r\n" +
                       "ПЕРЕДАТЬ ЧАТУ — опубликовать короткие ответы ANS по обработанным патчам в GitHub.\r\n\r\n" +
                       "СКОПИРОВАТЬ — скопировать короткое сообщение для вставки в ChatGPT.\r\n\r\n" +
                       "Выполняются только report-only и проверенное обновление исходников sync-main. " +
                       "Команды PowerShell из публичного GitHub не выполняются.";
    }

    private async Task ReceiveAndApplyAsync()
    {
        var history = PatchHistory.Load();
        var list = await BridgeGitHub.GetPatchesAsync();
        patches.DataSource = null;
        patches.DataSource = list;

        int completed = 0;
        int skipped = 0;
        foreach (var patch in list)
        {
            if (history.ContainsKey(patch.Number)) { skipped++; continue; }
            status.Text = $"{patch.Id}: применение...";
            string state;
            string technical;
            string summary;
            try
            {
                technical = await GitPatch.ApplyAsync(patch);
                state = "PASS";
                summary = patch.Action == "report-only" ? "Проверка выполнена; изменения не требовались"
                    : "Проверенное обновление исходников выполнено";
            }
            catch (Exception ex)
            {
                state = "FAIL";
                technical = ex.Message; // Local only. Never publish raw exception details.
                summary = "Патч не выполнен; подробности доступны локально";
            }
            history[patch.Number] = new PatchReceipt(patch.Number, patch.Id, state, summary,
                patch.Url, false, null);
            PatchHistory.Save(history); // Persist before moving to the next patch.
            completed++;
            details.Text = $"{patch.Id} · {state}\r\n\r\n{technical}\r\n\r\n" +
                           "Результат сохранён локально. Нажмите «ПЕРЕДАТЬ ЧАТУ» для публикации ANS.";
            if (state == "FAIL")
            {
                status.Text = $"{patch.Id}: FAIL. Следующие задания остановлены.";
                return; // Fail closed: later patches may depend on this one.
            }
        }

        if (completed == 0)
            details.Text = list.Count == 0 ? "Новых доверенных патчей нет." : "Новых патчей нет: все найденные уже обработаны.";
        status.Text = $"Новых обработано: {completed}. Ранее обработано: {skipped}. " +
                      $"Ответов к отправке: {history.Values.Count(x => !x.Posted)}.";
    }

    private async Task PublishAnswersAsync()
    {
        var history = PatchHistory.Load();
        var pending = history.Values.Where(x => !x.Posted).OrderBy(x => x.Number).ToList();
        if (pending.Count == 0)
        {
            status.Text = "Новых ответов для GitHub нет.";
            details.Text = "Сначала нажмите «ИЗ ЧАТА», если ожидаются новые патчи.";
            return;
        }
        int count = 0;
        foreach (var receipt in pending)
        {
            status.Text = $"Публикация ANS {receipt.Id}...";
            // Only the ID, status, and a vetted summary go into the public repository.
            string report = $"## ANS {receipt.Id} — {receipt.State}\n\n" +
                            $"Результат: {receipt.Summary}.\n" +
                            "Источник: uUnityBridge Desktop.\n";
            var url = await BridgeGitHub.SendAnswerAsync(receipt.Number, receipt.Id, report);
            history[receipt.Number] = receipt with
            {
                Posted = true,
                AnswerUrl = string.IsNullOrWhiteSpace(url) ? receipt.IssueUrl : url
            };
            PatchHistory.Save(history);
            count++;
        }
        var last = history.Values.Where(x => x.Posted).OrderByDescending(x => x.Number).First();
        status.Text = $"Отправлено ответов ANS: {count}. Последний: {last.Id} — {last.State}.";
        details.Text = $"ANS {last.Id} — {last.State}\r\n\r\n{last.AnswerUrl}\r\n\r\n" +
                       "Теперь нажмите «СКОПИРОВАТЬ» и вставьте короткий текст в чат.";
    }

    private void CopyNotification()
    {
        try
        {
            var history = PatchHistory.Load();
            var posted = history.Values.Where(x => x.Posted).OrderByDescending(x => x.Number).ToList();
            if (posted.Count == 0)
            {
                status.Text = "Сначала отправьте ответ через «ПЕРЕДАТЬ ЧАТУ».";
                return;
            }
            var last = posted[0];
            var text = $"uUnityBridge: PATCH {last.Id} — {last.State}. ANS {last.Id} опубликован: {last.AnswerUrl ?? last.IssueUrl}. Проверь ответ в GitHub.";
            Clipboard.SetText(text);
            status.Text = $"Скопировано сообщение по {last.Id}.";
            details.Text = text;
        }
        catch (Exception e)
        {
            status.Text = "Не удалось скопировать сообщение.";
            details.Text = e.Message;
        }
    }

    private void DescribeSelection()
    {
        if (patches.SelectedItem is not Patch patch) return;
        try
        {
            var history = PatchHistory.Load();
            history.TryGetValue(patch.Number, out var receipt);
            var state = receipt == null ? "НОВЫЙ" : receipt.State + (receipt.Posted ? ", ANS отправлен" : ", ANS ожидает отправки");
            details.Text = $"Патч: {patch.Id}\r\nСтатус: {state}\r\nТип: {patch.Action}\r\n\r\n" +
                           $"{patch.Description}\r\n\r\n{patch.Url}";
        }
        catch (Exception e) { details.Text = "Локальная история недоступна: " + e.Message; }
    }

    private async Task ExecuteUiAsync(Func<Task> work)
    {
        Toggle(false);
        status.Text = "Операция выполняется...";
        try { await work(); }
        catch (Exception e)
        {
            status.Text = "ОШИБКА: операция не завершена";
            details.Text = e.Message + "\r\n\r\nЛокальные данные сохранены; опубликованные ANS повторно не отправляются.";
        }
        finally { Toggle(true); }
    }

    private void Toggle(bool enabled)
    {
        receive.Enabled = enabled;
        send.Enabled = enabled;
        copy.Enabled = enabled;
        UseWaitCursor = !enabled;
    }
}
