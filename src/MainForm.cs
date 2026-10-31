using System.Diagnostics;
using System.Drawing;
using System.Text;

namespace uUnityBridge;

internal sealed class MainForm : Form
{
    private readonly Button send = BigButton("В ЧАТ  →", Color.FromArgb(31, 111, 81));
    private readonly Button receive = BigButton("←  ИЗ ЧАТА", Color.FromArgb(35, 75, 132));
    private readonly Button apply = BigButton("ПРИМЕНИТЬ ПАТЧ", Color.FromArgb(81, 62, 127));
    private readonly ListBox patches = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11), IntegralHeight = false };
    private readonly TextBox details = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10), BackColor = Color.FromArgb(23, 26, 34), ForeColor = Color.White, BorderStyle = BorderStyle.None };
    private readonly Label status = new() { Text = "Готово. GitHub: sunpole/uUnityBridge", Dock = DockStyle.Fill, Padding = new Padding(10), AutoEllipsis = true, Font = new Font("Segoe UI", 10) };

    private static Button BigButton(string text, Color color) => new()
    {
        Text = text, BackColor = color, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
        Font = new Font("Segoe UI Semibold", 12, FontStyle.Bold), Height = 65,
        Dock = DockStyle.Fill, Cursor = Cursors.Hand, TabStop = true
    };

    internal MainForm()
    {
        Text = "uUnity Bridge · GitHub ↔ компьютер";
        MinimumSize = new Size(740, 520);
        Size = new Size(940, 625);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(241, 244, 249);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 4, ColumnCount = 1 };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        grid.Controls.Add(new Label { Text = "uUnity Bridge  ·  PATCH / ANS", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 18, FontStyle.Bold) }, 0, 0);
        var buttons = new TableLayoutPanel { ColumnCount = 3, Dock = DockStyle.Fill };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        buttons.Controls.Add(send, 0, 0);
        buttons.Controls.Add(receive, 1, 0);
        buttons.Controls.Add(apply, 2, 0);
        grid.Controls.Add(buttons, 0, 1);
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(patches);
        split.Panel2.Controls.Add(details);
        grid.Controls.Add(split, 0, 2);
        grid.Controls.Add(status, 0, 3);
        Controls.Add(grid);
        Shown += (_, _) => { if (split.Width > 450) split.SplitterDistance = 310; };

        patches.SelectedIndexChanged += (_, _) => DescribeSelection();
        receive.Click += async (_, _) => await ExecuteUiAsync(async () =>
        {
            var list = await BridgeGitHub.GetPatchesAsync();
            patches.DataSource = null;
            patches.DataSource = list;
            status.Text = $"Найдено доверенных заданий: {list.Count}. Отображаются только задания автора {BridgeGitHub.TrustedAuthor}.";
            if (list.Count > 0) patches.SelectedIndex = 0;
            else details.Text = "Открытых доверенных патчей нет.\r\nПатчи создаются в GitHub Issues от имени владельца репозитория.";
        });
        send.Click += async (_, _) => await ExecuteUiAsync(async () =>
        {
            var selected = patches.SelectedItem as Patch;
            var text = await BuildSafeReportAsync(selected, "STATUS", "Диагностика по кнопке пользователя");
            var url = await BridgeGitHub.SendReportAsync(selected, text);
            status.Text = selected == null ? "Создан публичный отчёт в GitHub Issues." : $"Отправлен ответ {selected.Id} ANS.";
            details.Text = text + "\r\n\r\nGitHub: " + url;
        });
        apply.Click += async (_, _) => await ExecuteUiAsync(async () =>
        {
            if (patches.SelectedItem is not Patch selected)
                throw new InvalidOperationException("Сначала нажмите «ИЗ ЧАТА» и выберите патч.");
            string outcome;
            string state;
            try { outcome = await GitPatch.ApplyAsync(selected); state = "PASS"; }
            catch (Exception e) { outcome = "Патч отклонён или завершился ошибкой (локально): " + e.Message; state = "FAIL"; }
            details.Text = outcome;
            status.Text = $"{selected.Id}: {state}. Формируется краткий отчёт.";
            // Never publish raw stderr, paths, usernames or secrets in a public repository.
            string report = await BuildSafeReportAsync(selected, state, state == "PASS" ? "Git fast-forward завершён или изменение не требовалось" : "Операция не выполнена; локальный подробный журнал показан только на экране");
            await BridgeGitHub.SendReportAsync(selected, report);
            status.Text = $"{selected.Id}: {state}. Ответ ANS опубликован в GitHub Issues.";
        });
        details.Text = "1. «ИЗ ЧАТА» — получить задания PATCH с GitHub.\r\n\r\n2. «ПРИМЕНИТЬ ПАТЧ» — обновить исходники в доверенном репозитории с контрольной точкой Git.\r\n\r\n3. «В ЧАТ» — отправить обезличенный статус или ответ ANS в GitHub Issues.\r\n\r\nВАЖНО: публичный GitHub не служит безопасной консолью удалённого выполнения команд.";
    }

    private void DescribeSelection()
    {
        if (patches.SelectedItem is not Patch p) return;
        details.Text = $"Патч: {p.Id}\r\nIssue: #{p.Number}\r\nДействие: {p.Action}\r\nКоммит: {p.Commit ?? "—"}\r\n\r\n{p.Description}\r\n\r\nПеред синхронизацией проверяются ветка, удалённый репозиторий, чистота дерева и точный SHA коммита.\r\n\r\n{p.Url}";
    }

    private async Task ExecuteUiAsync(Func<Task> work)
    {
        Toggle(false);
        status.Text = "Операция выполняется...";
        try { await work(); }
        catch (Exception e)
        {
            status.Text = "ОШИБКА: операция не завершена";
            details.Text = e.Message + "\r\n\r\nЕсли GitHub CLI не авторизован, используйте штатный вход через gh auth login на компьютере.";
        }
        finally { Toggle(true); }
    }

    private void Toggle(bool enabled)
    {
        send.Enabled = enabled;
        receive.Enabled = enabled;
        apply.Enabled = enabled;
        UseWaitCursor = !enabled;
    }

    private static async Task<string> BuildSafeReportAsync(Patch? patch, string result, string note)
    {
        var b = new StringBuilder();
        b.AppendLine($"## ANS {(patch?.Id ?? "STATUS")} — {result}");
        b.AppendLine();
        b.AppendLine("Источник: uUnityBridge desktop, действие нажатием кнопки.");
        b.AppendLine($"Время UTC: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
        b.AppendLine($"ОС: {(Environment.Is64BitOperatingSystem ? "Windows x64" : "Windows x86")}");
        b.AppendLine($"Логических процессоров: {Environment.ProcessorCount}");
        b.AppendLine($"Приложение: {Application.ProductVersion}");
        b.AppendLine($"Результат: {note}");
        string gitState;
        try
        {
            var root = GitPatch.LocateRepository();
            gitState = root == null ? "Git-репозиторий приложения не найден" : (await ProcessRunner.RunAsync("git", ["-C", root, "rev-parse", "--short", "HEAD"], timeoutSeconds: 10));
        }
        catch { gitState = "Git недоступен"; }
        b.AppendLine($"Git HEAD: {gitState}");
        b.AppendLine();
        b.AppendLine("Пути, имя пользователя, hostname, содержимое файлов, переменные окружения и токены не отправляются.");
        return b.ToString();
    }
}
