using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace uUnityBridge;

/// <summary>
/// Auto-updates only from trusted official releases of sunpole/uUnityBridge.
/// All downloaded bytes are validated before a separate helper replaces the
/// EXE; public Issue contents never authorize a binary update.
/// </summary>
internal static class ReleaseUpdater
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uUnityBridge");
    private static readonly string UpdateRoot = Path.Combine(Root, "updates");
    private static readonly string HelperPath = Path.Combine(AppContext.BaseDirectory, "uUnityBridge.Updater.exe");
    private static readonly string TargetPath = Path.Combine(AppContext.BaseDirectory, "uUnityBridge.exe");

    internal static Version Current => Assembly.GetExecutingAssembly().GetName().Version
        ?? new Version(0, 0);

    internal static async Task<bool> CheckAndLaunchAsync()
    {
        // A legacy standalone executable has no updater companion: do not try
        // to replace it or assume installation permissions.
        if (!File.Exists(HelperPath) || !File.Exists(TargetPath)) return false;

        await BridgeGitHub.EnsureAuthAsync();
        string listJson = await ProcessRunner.RunAsync("gh",
            ["release", "list", "--exclude-drafts", "--exclude-pre-releases",
                "-R", BridgeGitHub.Repo, "--limit", "10", "--json", "tagName"], timeoutSeconds: 45);
        using var list = JsonDocument.Parse(listJson);
        string? tag = null;
        Version? newest = null;
        foreach (var rel in list.RootElement.EnumerateArray())
        {
            var t = rel.GetProperty("tagName").GetString() ?? "";
            if (!TryVersion(t, out var v) || v <= Current) continue;
            if (newest == null || v > newest) { tag = t; newest = v; }
        }
        if (tag == null) return false;

        string releaseJson = await ProcessRunner.RunAsync("gh",
            ["release", "view", tag, "-R", BridgeGitHub.Repo,
                "--json", "tagName,isDraft,isPrerelease,assets"], timeoutSeconds: 40);
        using var doc = JsonDocument.Parse(releaseJson);
        var release = doc.RootElement;
        if (release.GetProperty("isDraft").GetBoolean() ||
            release.GetProperty("isPrerelease").GetBoolean() ||
            release.GetProperty("tagName").GetString() != tag)
            throw new InvalidDataException("GitHub Release не является доверенным стабильным выпуском.");

        bool exePresent = false, sumPresent = false;
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (name is not ("uUnityBridge.exe" or "uUnityBridge.exe.sha256")) continue;
            long size = asset.GetProperty("size").GetInt64();
            if (name == "uUnityBridge.exe")
            {
                if (size is < 100_000 or > 150_000_000)
                    throw new InvalidDataException("Недопустимый размер EXE в релизе.");
                exePresent = true;
            }
            else
            {
                if (size is < 64 or > 1024) throw new InvalidDataException("Недопустимый файл контрольной суммы.");
                sumPresent = true;
            }
        }
        if (!exePresent || !sumPresent) throw new InvalidDataException("В выпуске отсутствует EXE или SHA256.");

        Directory.CreateDirectory(UpdateRoot);
        string stage = Path.Combine(UpdateRoot, tag);
        Directory.CreateDirectory(stage);
        string candidate = Path.Combine(stage, "uUnityBridge.exe");
        string sumFile = Path.Combine(stage, "uUnityBridge.exe.sha256");
        // Always download fresh to avoid trusting stale files in staging.
        if (File.Exists(candidate)) File.Delete(candidate);
        if (File.Exists(sumFile)) File.Delete(sumFile);
        await ProcessRunner.RunAsync("gh", ["release", "download", tag, "-R", BridgeGitHub.Repo,
            "--pattern", "uUnityBridge.exe", "--pattern", "uUnityBridge.exe.sha256",
            "--dir", stage], timeoutSeconds: 180);
        if (!File.Exists(candidate) || !File.Exists(sumFile))
            throw new IOException("Два обязательных файла выпуска не скачались.");
        string sumText = File.ReadAllText(sumFile).Trim();
        var match = Regex.Match(sumText,
            @"^(?<sha>[a-fA-F0-9]{64})(?:\s+\*?uUnityBridge\.exe)?$",
            RegexOptions.CultureInvariant);
        if (!match.Success) throw new InvalidDataException("SHA256 имеет неверный формат.");
        string expected = match.Groups["sha"].Value;
        using (var stream = File.OpenRead(candidate))
        {
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), expected,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA256 не совпадает. Обновление отклонено.");
        }

        var info = new ProcessStartInfo(HelperPath) { UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory };
        info.ArgumentList.Add("apply");
        info.ArgumentList.Add(TargetPath);
        info.ArgumentList.Add(Environment.ProcessId.ToString());
        info.ArgumentList.Add(expected);
        info.ArgumentList.Add(candidate);
        Process.Start(info);
        return true; // MainForm must exit so the helper can replace its EXE.
    }

    internal static bool BackupAvailable()
    {
        string manifest = Path.Combine(Root, "backups", "last-good.json");
        if (!File.Exists(manifest) || !File.Exists(HelperPath)) return false;
        try
        {
            using var d = JsonDocument.Parse(File.ReadAllText(manifest));
            return string.Equals(d.RootElement.GetProperty("TargetPath").GetString(),
                TargetPath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    internal static bool LaunchRollback()
    {
        if (!BackupAvailable()) return false;
        var info = new ProcessStartInfo(HelperPath) { UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory };
        info.ArgumentList.Add("rollback");
        info.ArgumentList.Add(TargetPath);
        info.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(info);
        return true;
    }

    internal static void ConfirmWindowReady(string[] args)
    {
        if (args.Length != 2 || args[0] != "--update-health-file") return;
        try
        {
            string candidate = Path.GetFullPath(args[1]);
            string allowed = Path.GetFullPath(UpdateRoot) + Path.DirectorySeparatorChar;
            if (candidate.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(candidate).StartsWith("ready-", StringComparison.Ordinal) &&
                Path.GetFileName(candidate).EndsWith(".txt", StringComparison.Ordinal))
                File.WriteAllText(candidate, "ready");
        }
        catch { /* Helper will rollback if confirmation cannot be written. */ }
    }

    private static bool TryVersion(string tag, out Version version)
    {
        version = new Version(0, 0);
        return Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$",
                RegexOptions.CultureInvariant) && Version.TryParse(tag[1..], out version);
    }
}
