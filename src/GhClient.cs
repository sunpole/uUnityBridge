using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace uUnityBridge;

internal static class ProcessRunner
{
    internal static async Task<string> RunAsync(string executable, IEnumerable<string> args, string? workingDirectory = null, int timeoutSeconds = 90)
    {
        using var p = new Process();
        p.StartInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (workingDirectory != null) p.StartInfo.WorkingDirectory = workingDirectory;
        foreach (var a in args) p.StartInfo.ArgumentList.Add(a);
        try { p.Start(); }
        catch (Exception e) { throw new InvalidOperationException($"Не удалось запустить {executable}: {e.Message}", e); }
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        var stdout = p.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = p.StandardError.ReadToEndAsync(cts.Token);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
            throw new TimeoutException($"Превышено время выполнения {executable} ({timeoutSeconds} c)");
        }
        var outText = await stdout;
        var errText = await stderr;
        if (p.ExitCode != 0) throw new InvalidOperationException($"{executable} завершился с кодом {p.ExitCode}: {errText.Trim()} {outText.Trim()}");
        return outText.Trim();
    }
}

internal sealed record Patch(int Number, string Id, string Title, string Description, string Action, string? Commit, string Url)
{
    public override string ToString() => $"{Id} · {Title}";
}

internal static class BridgeGitHub
{
    public const string Repo = "sunpole/uUnityBridge";
    public const string TrustedAuthor = "sunpole";

    internal static async Task EnsureAuthAsync()
    {
        await ProcessRunner.RunAsync("gh", ["auth", "status"], timeoutSeconds: 30);
        var login = await ProcessRunner.RunAsync("gh", ["api", "user", "--jq", ".login"], timeoutSeconds: 30);
        if (!string.Equals(login, TrustedAuthor, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Выполнен вход как {login}, требуется {TrustedAuthor}. Действие отменено.");
    }

    internal static async Task<List<Patch>> GetPatchesAsync()
    {
        await EnsureAuthAsync();
        string json = await ProcessRunner.RunAsync("gh", ["issue", "list", "-R", Repo, "--state", "open", "--limit", "100", "--json", "number,title,author,body,url"], timeoutSeconds: 45);
        var result = new List<Patch>();
        using var d = JsonDocument.Parse(json);
        foreach (var item in d.RootElement.EnumerateArray())
        {
            var author = item.GetProperty("author").GetProperty("login").GetString();
            if (!string.Equals(author, TrustedAuthor, StringComparison.OrdinalIgnoreCase)) continue;
            string title = item.GetProperty("title").GetString() ?? "";
            if (!title.StartsWith("[PATCH ", StringComparison.Ordinal)) continue;
            var body = item.GetProperty("body").GetString() ?? "";
            try
            {
                var jsonBody = ExtractJson(body);
                using var patchDoc = JsonDocument.Parse(jsonBody);
                var patch = patchDoc.RootElement;
                if (patch.GetProperty("schema").GetString() != "uub.patch.v1") continue;
                var id = patch.GetProperty("id").GetString() ?? "";
                var action = patch.GetProperty("action").GetString() ?? "";
                if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^P[0-9]{4,6}$")) continue;
                if (title != $"[PATCH {id}]") continue;
                if (action is not ("sync-main" or "report-only")) continue;
                var commit = patch.TryGetProperty("commit", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                if (action == "sync-main" && (commit == null || !System.Text.RegularExpressions.Regex.IsMatch(commit, "^[0-9a-fA-F]{40}$"))) continue;
                var description = patch.TryGetProperty("description", out var v) ? (v.GetString() ?? "") : "";
                result.Add(new Patch(item.GetProperty("number").GetInt32(), id, title, description, action, commit, item.GetProperty("url").GetString() ?? ""));
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { /* Invalid issue is not executable. */ }
        }
        return result.OrderBy(p => p.Number).ToList();
    }

    private static string ExtractJson(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            int firstLine = s.IndexOf('\n');
            int lastFence = s.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLine < 0 || lastFence <= firstLine) throw new JsonException("Некорректный JSON-блок");
            return s.Substring(firstLine + 1, lastFence - firstLine - 1).Trim();
        }
        return s;
    }

    internal static async Task<string> SendReportAsync(Patch? patch, string report)
    {
        await EnsureAuthAsync();
        if (patch == null)
        {
            string title = "[REPORT] " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
            return await ProcessRunner.RunAsync("gh", ["issue", "create", "-R", Repo, "--title", title, "--body", report], timeoutSeconds: 50);
        }
        return await ProcessRunner.RunAsync("gh", ["issue", "comment", patch.Number.ToString(), "-R", Repo, "--body", report], timeoutSeconds: 50);
    }
}

internal static class GitPatch
{
    internal static string? LocateRepository()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int n = 0; dir != null && n < 8; n++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
        return null;
    }

    private static Task<string> Git(string root, params string[] args) =>
        ProcessRunner.RunAsync("git", ["-C", root, .. args], timeoutSeconds: 120);

    internal static async Task<string> ApplyAsync(Patch patch)
    {
        if (patch.Action == "report-only") return "Задание только для отчёта, изменений нет.";
        if (patch.Action != "sync-main" || patch.Commit is null) throw new InvalidOperationException("Неизвестный тип патча");
        var root = LocateRepository() ?? throw new InvalidOperationException("Каталог Git репозитория рядом с EXE не найден.");
        string remote = (await Git(root, "remote", "get-url", "origin")).Trim().TrimEnd('/');
        if (remote is not ("https://github.com/sunpole/uUnityBridge.git" or "https://github.com/sunpole/uUnityBridge" or "git@github.com:sunpole/uUnityBridge.git"))
            throw new InvalidOperationException("Git remote origin не соответствует разрешённому репозиторию.");
        var branch = await Git(root, "branch", "--show-current");
        if (branch != "main") throw new InvalidOperationException("Для обновления нужна ветка main.");
        var dirty = await Git(root, "status", "--porcelain", "--untracked-files=normal");
        if (!string.IsNullOrWhiteSpace(dirty)) throw new InvalidOperationException("Есть локальные изменения: патч не применяется во избежание потери данных.");
        await Git(root, "fetch", "--no-tags", "origin", "main");
        string actual = await Git(root, "rev-parse", "refs/remotes/origin/main");
        if (!string.Equals(actual, patch.Commit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SHA не совпал: ожидался {patch.Commit}, в origin/main сейчас {actual}. Патч не применяется.");
        string before = await Git(root, "rev-parse", "HEAD");
        if (string.Equals(before, actual, StringComparison.OrdinalIgnoreCase)) return "Патч уже находится в локальной main: " + actual;
        string checkpoint = $"checkpoint/{patch.Id}-{DateTime.Now:yyyyMMdd-HHmmss}";
        await Git(root, "branch", checkpoint, before);
        await Git(root, "merge", "--ff-only", actual);
        return $"Успешное fast-forward обновление. До: {before}; После: {actual}; Откат: {checkpoint}. Выполнение кода/установки не запускалось.";
    }
}
