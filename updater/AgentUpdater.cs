using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace uUnityBridge.Updater;

// Helper invoked only by a local GUI-confirmed bootstrap from a verified
// stable Release. No GitHub Issue / command text controls its target.
internal static class AgentUpdater
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "uUnityBridge");
    private static readonly string Staging = Path.Combine(Root, "updates", "agent-bootstrap");
    private static readonly string Approval = Path.Combine(Staging, "approval.json");
    private static readonly string Backups = Path.Combine(Root, "agent-backups");
    private static readonly string ResultLog = Path.Combine(Root, "agent-updater-last.txt");
    private sealed record ApprovalRecord(
        string Schema, string Target, string Sha256, string Nonce,
        string Stage, DateTimeOffset ExpiresUtc);
    private static readonly JsonSerializerOptions Options =
        new() { PropertyNameCaseInsensitive = true };

    internal static int Run(string targetArg, string shaArg, string nonceArg)
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException("Elevated installation refused.");
            using var mutex = new Mutex(false, @"Local\uUnityBridge.AgentUpgrade.v1");
            if (!mutex.WaitOne(TimeSpan.FromSeconds(1)))
                throw new InvalidOperationException("Another agent update is active.");
            try
            {
                Apply(targetArg, shaArg, nonceArg);
                Log("PASS: agent_updated_and_health_verified");
                return 0;
            }
            finally { mutex.ReleaseMutex(); }
        }
        catch (Exception ex)
        {
            Log("FAIL: " + ex.GetType().Name);
            return 1;
        }
    }

    private static void Apply(string targetArg, string shaArg, string nonceArg)
    {
        if (!File.Exists(Approval))
            throw new InvalidDataException("Missing one-time local authorization.");
        var approval = JsonSerializer.Deserialize<ApprovalRecord>(
            File.ReadAllText(Approval), Options) ??
            throw new InvalidDataException("Invalid local authorization.");
        string stage = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        string target = Path.GetFullPath(targetArg);
        string stageRoot = Path.GetFullPath(Staging).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (approval.Schema != "uub.agent.bootstrap.v1" ||
            approval.Nonce != nonceArg ||
            approval.Nonce.Length != 32 ||
            !Regex.IsMatch(approval.Nonce, @"^[a-f0-9]{32}$") ||
            approval.ExpiresUtc <= DateTimeOffset.UtcNow ||
            approval.ExpiresUtc > DateTimeOffset.UtcNow.AddMinutes(6) ||
            !string.Equals(Path.GetFullPath(approval.Target), target,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(approval.Stage)), stage,
                StringComparison.OrdinalIgnoreCase) ||
            !stage.StartsWith(stageRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(approval.Sha256, shaArg, StringComparison.Ordinal) ||
            !Regex.IsMatch(shaArg, @"^[a-f0-9]{64}$") ||
            Path.GetFileName(target) != "uUnityBridge.Agent.exe" ||
            !File.Exists(target))
            throw new InvalidDataException("Agent update authorization mismatch.");

        RequireSafeTarget(target);
        RequireUnlinked(stage);
        string candidate = Path.Combine(stage, "uUnityBridge.Agent.exe");
        if (!File.Exists(candidate) || Hash(candidate) != shaArg)
            throw new InvalidDataException("Staged agent does not match release checksum.");
        var version = FileVersionInfo.GetVersionInfo(candidate).FileVersion;
        var oldVersion = FileVersionInfo.GetVersionInfo(target).FileVersion;
        if (!Version.TryParse(version, out var nextVersion) ||
            nextVersion < new Version(0, 3, 0) ||
            (Version.TryParse(oldVersion, out var previousVersion) &&
             nextVersion <= previousVersion))
            throw new InvalidDataException("Agent version is not a valid upgrade.");

        // A one-time approval cannot be replayed; failed attempts require fresh
        // explicit confirmation in the Desktop GUI.
        File.Delete(Approval);
        _ = AgentUpgradeTransaction.Apply(target, candidate, shaArg, Backups,
            stopExactProcess: () => StopExactAgent(target),
            launchAndProbeHealth: () =>
            {
                using var started = LaunchAgent(target);
                return WaitForHealth(target, started, nextVersion);
            },
            restartPrevious: () =>
            {
                using var restarted = LaunchAgent(target);
            });
    }

    private static Process LaunchAgent(string file)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(file)!
        };
        info.ArgumentList.Add("--serve");
        return Process.Start(info) ??
            throw new IOException("Cannot launch the approved agent.");
    }

    private static bool WaitForHealth(string file, Process server, Version required)
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            if (server.HasExited) return false;
            Thread.Sleep(600);
            using var check = new Process();
            check.StartInfo = new ProcessStartInfo(file)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            check.StartInfo.ArgumentList.Add("--request");
            check.StartInfo.ArgumentList.Add("health");
            check.Start();
            if (!check.WaitForExit(5000))
            {
                check.Kill(entireProcessTree: true);
                check.WaitForExit(5000);
                continue;
            }
            string reply = check.StandardOutput.ReadToEnd();
            if (check.ExitCode != 0 || reply.Length > 8192) continue;
            try
            {
                using var json = JsonDocument.Parse(reply);
                var root = json.RootElement;
                string? v = root.GetProperty("version").GetString();
                if (root.GetProperty("status").GetString() == "ok" &&
                    root.GetProperty("service").GetString() == "uUnityBridge.Agent" &&
                    Version.TryParse(v?.Replace("-preview", ""), out var actual) &&
                    new Version(actual.Major, actual.Minor, actual.Build) >=
                    new Version(required.Major, required.Minor, required.Build))
                    return true;
            }
            catch (JsonException) { }
        }
        return false;
    }

    private static void StopExactAgent(string target)
    {
        int session = Process.GetCurrentProcess().SessionId;
        foreach (var p in Process.GetProcessesByName("uUnityBridge.Agent"))
        {
            using (p)
            {
                try
                {
                    if (p.SessionId != session ||
                        !string.Equals(p.MainModule?.FileName, target,
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    p.Kill(entireProcessTree: true);
                    if (!p.WaitForExit(10000))
                        throw new IOException("Old agent did not stop.");
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Refuse updating a process whose identity cannot be established.
                    throw new InvalidOperationException("Agent process identity inaccessible.");
                }
            }
        }
    }

    private static void RequireSafeTarget(string file)
    {
        string full = Path.GetFullPath(file);
        RequireUnlinked(full);
        if (full.StartsWith(Path.GetFullPath(Staging) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Cannot install into temporary staging.");
        foreach (string forbidden in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        })
        {
            if (string.IsNullOrEmpty(forbidden)) continue;
            string path = Path.GetFullPath(forbidden).TrimEnd(Path.DirectorySeparatorChar);
            if (full.StartsWith(path + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(full, path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("System or shared folder is forbidden.");
        }
    }

    private static void RequireUnlinked(string path)
    {
        string full = Path.GetFullPath(path);
        for (string? cursor = full; cursor is not null; cursor = Path.GetDirectoryName(cursor))
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor)) &&
                File.GetAttributes(cursor).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Reparse points not allowed.");
            if (Path.GetPathRoot(cursor) == cursor) break;
        }
    }

    private static string Hash(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    private static void Log(string result)
    {
        try
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(ResultLog,
                DateTimeOffset.UtcNow.ToString("O") + " " + result);
        }
        catch { /* Logging must not mask the update error. */ }
    }
}
