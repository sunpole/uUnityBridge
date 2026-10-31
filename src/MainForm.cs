using System.Drawing;
using System.Text;

namespace uUnityBridge;

internal sealed class MainForm : Form
{
    private readonly Button copy = BigButton("ОТЧЁТ · НОВЫХ НЕТ", Color.FromArgb(31, 111, 81));
    private readonly System.Windows.Forms.Timer poller = new() { Interval = 60_000 };
    private bool polling;
    private int consecutiveNetworkErrors;
    private DateTimeOffset nextPollUtc = DateTimeOffset.MinValue;
    private DateTimeOffset nextUpdateCheckUtc = DateTimeOffset.MinValue;
    private bool updateChecking;
    private readonly LinkLabel rollback = new()
    {
        Text = "↶ Откат версии", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight,
        LinkColor = Color.FromArgb(68, 80, 118), AutoSize = false,
        Font = new Font("Segoe UI", 10)
    };
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
        Text = "uUnity Bridge · PATCH / ANS · 0.4.0";
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
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        header.Controls.Add(new Label { Text = "uUnity Bridge  ·  PATCH / ANS", Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 18, FontStyle.Bold) }, 0, 0);
        header.Controls.Add(rollback, 1, 0);
        rollback.Visible = ReleaseUpdater.BackupAvailable();
        rollback.LinkClicked += (_, _) =>
        {
            try
            {
                if (!ReleaseUpdater.LaunchRollback()) return;
                poller.Stop();
                Close();
            }
            catch (Exception ex)
            {
                status.Text = "Откат не запущен";
                details.Text = ex.Message;
            }
        };
        grid.Controls.Add(header, 0, 0);
        var buttons = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Fill };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.Controls.Add(copy, 0, 0);
        grid.Controls.Add(buttons, 0, 1);
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(patches);
        split.Panel2.Controls.Add(details);
        grid.Controls.Add(split, 0, 2);
        grid.Controls.Add(status, 0, 3);
        Controls.Add(grid);
        copy.Enabled = false;
        copy.Click += (_, _) => CopyNotification();
        poller.Tick += async (_, _) =>
        {
            if (polling || updateChecking) return;
            if (DateTimeOffset.UtcNow >= nextPollUtc) await PollOnceAsync();
            if (DateTimeOffset.UtcNow >= nextUpdateCheckUtc) await CheckForUpdateAsync();
        };
        FormClosed += (_, _) => poller.Stop();

        Shown += async (_, _) =>
        {
            if (split.Width > 450) split.SplitterDistance = 310;
            try
            {
                var history = PatchHistory.Load();
                ReportInbox.LoadOrInitialize(history); // Baseline legacy v0.2.1 ANS only once.
                RefreshReportButton(history);
                details.Text = "Автоматическая синхронизация включена. Проверка GitHub каждые 60 секунд.\r\n" +
                    "Новые разрешённые PATCH применяются, ответы ANS публикуются, завершённые Issues закрываются.\r\n" +
                    "Кнопка ОТЧЁТ доступна только при новых опубликованных результатах.\r\n" +
                    "В публичном GitHub не выполняются произвольные команды или установщики.";
                ReleaseUpdater.ConfirmWindowReady(Environment.GetCommandLineArgs().Skip(1).ToArray());
                await PollOnceAsync();
                await CheckForUpdateAsync();
                poller.Start();
            }
            catch (Exception e)
            {
                status.Text = "Ошибка локальной истории: автоматическая обработка остановлена";
                details.Text = e.Message;
            }
        };
    }

    private async Task PollOnceAsync()
    {
        if (polling || IsDisposed || Disposing || DateTimeOffset.UtcNow < nextPollUtc) return;
        polling = true;
        poller.Stop();
        copy.Enabled = false;
        try
        {
            await ReceiveAndApplyAsync();
            await PublishAnswersAsync();
            consecutiveNetworkErrors = 0;
            nextPollUtc = DateTimeOffset.UtcNow.AddSeconds(60);
            var history = PatchHistory.Load();
            RefreshReportButton(history);
            int news = ReportInbox.Pending(history.Values, ReportInbox.LoadOrInitialize(history)).Count;
            status.Text = news == 0
                ? $"GitHub проверен: {DateTime.Now:HH:mm:ss}. Новых отчётов нет. Следующая проверка через 60 с."
                : $"Готово: {news} новых ANS. Нажми «ОТЧЁТ» и передай текст в ChatGPT.";
        }
        catch (Exception e)
        {
            consecutiveNetworkErrors++;
            int minutes = Math.Min(30, 1 << Math.Min(consecutiveNetworkErrors, 5));
            nextPollUtc = DateTimeOffset.UtcNow.AddMinutes(minutes);
            status.Text = $"Проверка не удалась · повтор не ранее чем через {minutes} мин.";
            details.Text = e.Message + "\r\n\r\nДействия в GitHub не повторяются вне контроля истории.";
            try { RefreshReportButton(PatchHistory.Load()); } catch { /* Fail closed. */ }
        }
        finally
        {
            polling = false;
            if (!IsDisposed && !Disposing)
            {
                try { RefreshReportButton(PatchHistory.Load()); }
                catch { copy.Enabled = false; }
                poller.Start();
            }
        }
    }

    private async Task CheckForUpdateAsync()
    {
        if (updateChecking || polling || DateTimeOffset.UtcNow < nextUpdateCheckUtc) return;
        updateChecking = true;
        poller.Stop();
        // A release is a separate trusted distribution channel, never an Issue action.
        nextUpdateCheckUtc = DateTimeOffset.UtcNow.AddMinutes(30);
        try
        {
            if (await ReleaseUpdater.CheckAndLaunchAsync())
            {
                status.Text = "Подготовлено проверенное обновление EXE. Перезапуск...";
                Close();
                return;
            }
            rollback.Visible = ReleaseUpdater.BackupAvailable();
        }
        catch (Exception e)
        {
            // Never terminate the working version due to a release/network error.
            nextUpdateCheckUtc = DateTimeOffset.UtcNow.AddMinutes(30);
            details.Text = "Проверка обновлений: " + e.Message +
                "\\r\\n\\r\\nТекущая версия продолжает работать.";
        }
        finally
        {
            updateChecking = false;
            if (!IsDisposed && !Disposing) poller.Start();
        }
    }

    private void RefreshReportButton(Dictionary<int, PatchReceipt> history)
    {
        var unseen = ReportInbox.Pending(history.Values, ReportInbox.LoadOrInitialize(history));
        copy.Enabled = !polling && unseen.Count != 0;
        copy.Text = unseen.Count == 0 ? "ОТЧЁТ · НОВЫХ НЕТ" : $"ОТЧЁТ · {unseen.Count} НОВЫХ";
        copy.BackColor = unseen.Count == 0 ? Color.FromArgb(90, 100, 115) : Color.FromArgb(31, 111, 81);
    }

    private async Task ReceiveAndApplyAsync()
    {
        var history = PatchHistory.Load();
        var list = await BridgeGitHub.GetPatchesAsync();
        if (list.GroupBy(p => p.Id).Any(g => g.Count() != 1))
            throw new InvalidOperationException("В GitHub обнаружены повторные идентификаторы PATCH. Ничего не применено.");
        if (list.Count > 5)
            throw new InvalidOperationException("Открыто более пяти PATCH: очередь переполнена. Сначала завершите текущие задания.");
        patches.DataSource = null;
        patches.DataSource = list;

        int completed = 0;
        int skipped = 0;
        foreach (var patch in list)
        {
            history.TryGetValue(patch.Number, out var previous);
            if (previous != null && !PatchWorkflow.Matches(patch, previous))
            {
                status.Text = $"{patch.Id}: CONFLICT. Обработка остановлена.";
                details.Text = "Патч был изменён после получения либо история не соответствует Issue. " +
                    "Автоматическое повторное применение запрещено. Проверьте историю локально.";
                return;
            }
            if (previous?.State == "APPLYING")
            {
                history[patch.Number] = previous with
                {
                    State = "NEEDS_REVIEW",
                    Summary = "Прошлый запуск прервался во время применения: требуется проверка Git",
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                PatchHistory.Save(history);
                status.Text = $"{patch.Id}: NEEDS_REVIEW. Очередь остановлена.";
                details.Text = "Возможное прерывание во время применения. Никаких повторных изменений не выполнено.";
                return;
            }
            if (previous?.State is "BLOCKED" or "NEEDS_REVIEW")
            {
                status.Text = $"{patch.Id}: {previous.State}. Очередь остановлена.";
                details.Text = "Для продолжения требуется устранить причину блокировки. Автоматический сброс истории запрещён.";
                return;
            }
            // Automatic retries are delayed; polling must not consume all three attempts
            // within a few minutes for a persistent Git/network problem.
            if (previous?.State == "FAIL" && previous.UpdatedAt.HasValue)
            {
                var delay = previous.Attempts == 1 ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(15);
                if (DateTimeOffset.UtcNow - previous.UpdatedAt.Value < delay)
                {
                    status.Text = $"{patch.Id}: ожидание повторной попытки после ошибки.";
                    return; // Keep dependent tasks blocked until this issue is resolved.
                }
            }
            if (!PatchWorkflow.ShouldAttempt(previous))
            {
                skipped++;
                continue;
            }

            int attempt = (previous?.Attempts ?? 0) + 1;
            var receipt = new PatchReceipt(patch.Number, patch.Id, "APPLYING",
                "Применение ещё не завершено", patch.Url, false, null,
                PatchWorkflow.Fingerprint(patch), attempt, DateTimeOffset.UtcNow);
            history[patch.Number] = receipt;
            PatchHistory.Save(history); // Persist intent BEFORE changing Git.

            status.Text = $"{patch.Id}: попытка {attempt}/{PatchWorkflow.MaxAttempts}...";
            string state, technical, summary;
            try
            {
                technical = await GitPatch.ApplyAsync(patch);
                state = patch.Action == "report-only" ? "PASS" : "SOURCE_UPDATED";
                summary = patch.Action == "report-only"
                    ? "Проверка выполнена; файловых изменений нет"
                    : "Исходники обновлены; EXE и QA не запускались";
            }
            catch (Exception ex)
            {
                technical = ex.Message; // Local only, never published verbatim.
                state = attempt >= PatchWorkflow.MaxAttempts ? "BLOCKED" : "FAIL";
                summary = state == "BLOCKED"
                    ? "Три попытки неуспешны; автоматические повторы остановлены"
                    : "Ошибка применения; повтор будет выполнен после паузы";
            }

            history[patch.Number] = receipt with
            {
                State = state,
                Summary = summary,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            PatchHistory.Save(history);
            completed++;
            details.Text = $"{patch.Id} · {state}\r\nПопытка {attempt}/{PatchWorkflow.MaxAttempts}\r\n\r\n" +
                technical + "\r\n\r\nПодробная ошибка остаётся только на этом компьютере.";

            if (state is "FAIL" or "BLOCKED")
            {
                status.Text = $"{patch.Id}: {state}. Следующие задания остановлены.";
                return;
            }
        }
        if (completed == 0)
            details.Text = list.Count == 0 ? "Открытых патчей нет." : "Новых патчей нет: всё уже обработано.";
        status.Text = $"Обработано: {completed}. Пропущено: {skipped}. " +
            $"Готовых ANS: {history.Values.Count(r => !r.Posted && PatchWorkflow.IsSendable(r))}.";
    }

    private async Task PublishAnswersAsync()
    {
        var history = PatchHistory.Load();
        var pending = history.Values.Where(r => !r.Posted && PatchWorkflow.IsSendable(r))
            .OrderBy(r => r.Number).ToList();
        int sent = 0, closed = 0;
        foreach (var receipt in pending)
        {
            status.Text = $"Отправка ANS {receipt.Id}...";
            string report = $"## ANS {receipt.Id} — {receipt.State}\n\n" +
                $"Результат: {receipt.Summary}.\n" +
                $"Попытка: {receipt.Attempts}/{PatchWorkflow.MaxAttempts}.\n" +
                "Источник: uUnityBridge Desktop.\n";
            string marker = PatchWorkflow.AnswerMarker(receipt);
            var url = await BridgeGitHub.SendAnswerAsync(receipt.Number, receipt.Id, report, marker);
            history[receipt.Number] = receipt with
            {
                Posted = true,
                AnswerUrl = string.IsNullOrWhiteSpace(url) ? receipt.IssueUrl : url,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            PatchHistory.Save(history); // Never post again on a retry after a partial success.
            sent++;
        }

        // Separate closing from publishing. A network failure during closing must not
        // cause an already-published ANS to be duplicated.
        foreach (var receipt in history.Values.Where(r =>
            r.Posted && !r.Closed && (PatchWorkflow.IsSuccess(r) || r.State == "BLOCKED")).OrderBy(r => r.Number).ToList())
        {
            status.Text = $"Закрытие завершённого PATCH {receipt.Id}...";
            await BridgeGitHub.CloseFinishedPatchAsync(receipt.Number, receipt.Id, PatchWorkflow.IsSuccess(receipt));
            history[receipt.Number] = receipt with { Closed = true, UpdatedAt = DateTimeOffset.UtcNow };
            PatchHistory.Save(history);
            closed++;
        }

        var last = history.Values.Where(r => r.Posted).OrderByDescending(r => r.Number).FirstOrDefault();
        status.Text = $"ANS отправлено: {sent}. Issues закрыто: {closed}.";
        details.Text = last == null ? "Новых ответов нет." :
            $"ANS {last.Id} — {last.State}\r\n\r\n{last.AnswerUrl}\r\n\r\n" +
            "Нажмите «ОТЧЁТ», чтобы скопировать новые ответы для ChatGPT.";
    }

    private void CopyNotification()
    {
        if (polling) return;
        try
        {
            var history = PatchHistory.Load();
            var alreadyCopied = ReportInbox.LoadOrInitialize(history);
            var pending = ReportInbox.Pending(history.Values, alreadyCopied);
            if (pending.Count == 0)
            {
                RefreshReportButton(history);
                status.Text = "Нет новых результатов после прошлого копирования.";
                return;
            }

            string summary = string.Join("; ", pending.Select(r =>
                $"{r.Id} — {r.State} ({r.AnswerUrl ?? r.IssueUrl})"));
            string text = $"uUnityBridge: {pending.Count} новых результатов после прошлого отчёта. " +
                summary + ". Проверь ответы в GitHub.";
            Clipboard.SetText(text); // The cursor advances only AFTER successful clipboard write.
            foreach (var receipt in pending) alreadyCopied.Add(ReportInbox.Key(receipt));
            ReportInbox.Save(alreadyCopied);
            RefreshReportButton(history);
            status.Text = $"Скопировано новых ANS: {pending.Count}. Передай текст ChatGPT.";
            details.Text = text;
        }
        catch (Exception e)
        {
            status.Text = "Ошибка формирования отчёта: ничего не отмечено переданным.";
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

}
