using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace uUnityBridge;

// One-time local bootstrap. The existing 0.4.2 Desktop can receive the next
// stable Desktop release using its old updater; this button then obtains the
// verified agent and a NEW updater helper from that same release.
// No GitHub Issue may invoke this method or select the target path.
internal static class AgentBootstrapper
{
    private const string Repo = "sunpole/uUnityBridge";
    private static readonly string UpdateRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "uUnityBridge", "updates", "agent-bootstrap");
    private sealed record Asset(string Name, string Sha, long Size);

    internal static async Task<string> StartAsync(IWin32Window owner)
    {
        using (var identity = WindowsIdentity.GetCurrent())
        {
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                return "BLOCKED: запустите uUnityBridge от обычного пользователя.";
        }
        string target = Path.Combine(AppContext.BaseDirectory, "uUnityBridge.Agent.exe");
        if (!File.Exists(target))
        {
            using var picker = new OpenFileDialog
            {
                Title = "Выберите уже установленный uUnityBridge.Agent.exe",
                Filter = "uUnityBridge.Agent.exe|uUnityBridge.Agent.exe",
                CheckFileExists = true, Multiselect = false
            };
            if (picker.ShowDialog(owner) != DialogResult.OK) return "Отменено.";
            target = picker.FileName;
        }
        target = Path.GetFullPath(target);
        if (Path.GetFileName(target) != "uUnityBridge.Agent.exe" ||
            !File.Exists(target) || File.GetAttributes(target).HasFlag(FileAttributes.ReparsePoint))
            return "BLOCKED: неподтверждённый путь агента.";

        if (MessageBox.Show(owner,
            "Разрешить однократное обновление только выбранного uUnityBridge.Agent.exe?\n" +
            "Программа загрузит два проверенных файла из стабильного GitHub Release, " +
            "сохранит резервную копию, остановит только выбранный агент, " +
            "заменит его и проверит запуск. При ошибке восстановит старую версию.\n\n" +
            "Папка назначения: " + Path.GetDirectoryName(target),
            "Обновление агента — локальное подтверждение",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return "Отменено.";

        await BridgeGitHub.EnsureAuthAsync();
        string json = await ProcessRunner.RunAsync("gh",
            ["api", "repos/" + Repo + "/releases/latest"], timeoutSeconds: 35);
        using var releaseDoc = JsonDocument.Parse(json);
        var release = releaseDoc.RootElement;
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        if (release.GetProperty("draft").GetBoolean() ||
            release.GetProperty("prerelease").GetBoolean() ||
            !Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$") ||
            !Version.TryParse(tag[1..], out Version? version) ||
            version < new Version(ReleaseUpdater.Current.Major, ReleaseUpdater.Current.Minor, ReleaseUpdater.Current.Build))
            return "BLOCKED: нет подходящего стабильного Release.";

        var assets = release.GetProperty("assets").EnumerateArray().ToArray();
        Asset FindAsset(string name)
        {
            var matches = assets.Where(a => a.GetProperty("name").GetString() == name).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("В релизе отсутствует нужный файл.");
            var a = matches[0];
            string digest = a.GetProperty("digest").GetString() ?? "";
            if (!Regex.IsMatch(digest, @"^sha256:[a-f0-9]{64}$") ||
                a.GetProperty("size").GetInt64() is < 100_000 or > 160_000_000)
                throw new InvalidDataException("У релиза нет проверяемой контрольной суммы.");
            return new Asset(name, digest[7..], a.GetProperty("size").GetInt64());
        }

        var agent = FindAsset("uUnityBridge.Agent.exe");
        var helper = FindAsset("uUnityBridge.Updater.exe");

        string sha = (await ProcessRunner.RunAsync("gh",
            ["api", "repos/" + Repo + "/commits/" + tag,
                "--jq", ".sha"], timeoutSeconds: 35)).Trim();
        if (!Regex.IsMatch(sha, @"^[a-f0-9]{40}$"))
            return "BLOCKED: некорректный commit релиза.";
        string runsJson = await ProcessRunner.RunAsync("gh",
            ["api", "repos/" + Repo + "/actions/runs?head_sha=" + sha + "&per_page=30"],
            timeoutSeconds: 35);
        using var runsDoc = JsonDocument.Parse(runsJson);
        bool trustedRun = runsDoc.RootElement.GetProperty("workflow_runs")
            .EnumerateArray().Any(x =>
                x.GetProperty("conclusion").GetString() == "success" &&
                x.GetProperty("event").GetString() == "workflow_dispatch" &&
                x.GetProperty("head_branch").GetString() == "main" &&
                x.GetProperty("head_sha").GetString() == sha &&
                x.GetProperty("path").GetString() == ".github/workflows/release-windows.yml" &&
                x.GetProperty("repository").GetProperty("full_name").GetString() == Repo);
        if (!trustedRun)
            return "BLOCKED: нет подтверждённого Release workflow для commit.";

        string stage = Path.Combine(UpdateRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        await ProcessRunner.RunAsync("gh", ["release", "download", tag, "-R", Repo,
            "--pattern", agent.Name, "--pattern", helper.Name, "--dir", stage],
            timeoutSeconds: 360);

        foreach (var asset in new[] { agent, helper })
        {
            string file = Path.Combine(stage, asset.Name);
            if (!File.Exists(file) || new FileInfo(file).Length != asset.Size ||
                !string.Equals(Hash(file), asset.Sha, StringComparison.Ordinal))
                return "BLOCKED: файл Release не прошёл SHA-256 проверку.";
        }

        var installedVersion = FileVersionInfo.GetVersionInfo(
            Path.Combine(stage, agent.Name)).FileVersion;
        if (!Version.TryParse(installedVersion, out var parsed) ||
            parsed < new Version(0, 3, 0))
            return "BLOCKED: версия нового агента не поддерживает P0010.";

        Directory.CreateDirectory(UpdateRoot);
        string nonce = Guid.NewGuid().ToString("N");
        string approval = Path.Combine(UpdateRoot, "approval.json");
        await File.WriteAllTextAsync(approval, JsonSerializer.Serialize(new
        {
            schema = "uub.agent.bootstrap.v1",
            target, sha256 = agent.Sha, nonce, stage,
            expiresUtc = DateTimeOffset.UtcNow.AddMinutes(5)
        }));

        var start = new ProcessStartInfo(Path.Combine(stage, helper.Name))
        {
            UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = stage
        };
        start.ArgumentList.Add("agent-apply");
        start.ArgumentList.Add(target);
        start.ArgumentList.Add(agent.Sha);
        start.ArgumentList.Add(nonce);
        _ = Process.Start(start) ?? throw new IOException("Не удалось запустить проверенный helper.");
        return "Обновление передано helper. Итог будет записан в " +
            "%LOCALAPPDATA%\\uUnityBridge\\agent-updater-last.txt. " +
            "Не запускайте второе обновление одновременно.";
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }
}
