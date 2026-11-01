using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace uUnityBridge.Updater;

internal sealed record LastGood(string BackupPath, string BackupSha256, string TargetPath, string UpdatedAtUtc);

internal static class Program
{
    private static readonly string AppData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uUnityBridge");
    private static readonly string BackupDir = Path.Combine(AppData, "backups");
    private static readonly string LastGoodPath = Path.Combine(BackupDir, "last-good.json");
    private static readonly string LogPath = Path.Combine(AppData, "updater-last.txt");
    private static readonly string BlockedShaPath = Path.Combine(AppData, "updates", "blocked-sha256.txt");

    // Call only with fixed local arguments produced by the desktop client.
    static int Main(string[] args)
    {
        try
        {
            if (args is ["agent-apply", var targetAgent, var sha256, var nonce])
                return AgentUpdater.Run(targetAgent, sha256, nonce);
            if (args.Length < 3 || args[0] is not ("apply" or "rollback"))
                throw new InvalidDataException("Неверный режим обновлятора.");
            string mode = args[0];
            string target = Path.GetFullPath(args[1]);
            if (Path.GetFileName(target) != "uUnityBridge.exe" ||
                !string.Equals(Path.GetDirectoryName(target),
                    Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Обновлять можно только соседний uUnityBridge.exe.");

            if (!int.TryParse(args[2], out int parentPid) || parentPid < 1)
                throw new InvalidDataException("Неверный PID.");
            WaitForExit(parentPid);
            if (mode == "apply")
            {
                if (args.Length != 5) throw new InvalidDataException("Ожидается SHA256 и путь скачанного EXE.");
                Apply(target, args[3], args[4]);
            }
            else
            {
                if (args.Length != 3) throw new InvalidDataException("Неверные аргументы отката.");
                Rollback(target);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Log("FAILED: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static void Apply(string target, string expectedSha, string staged)
    {
        if (expectedSha.Length != 64 || !expectedSha.All(Uri.IsHexDigit))
            throw new InvalidDataException("Неверная контрольная сумма.");
        string fullStage = Path.GetFullPath(staged);
        string allowed = Path.GetFullPath(Path.Combine(AppData, "updates")) + Path.DirectorySeparatorChar;
        if (!fullStage.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(fullStage) != "uUnityBridge.exe" ||
            !File.Exists(fullStage))
            throw new InvalidDataException("Обновление доступно только из локального staging.");
        string actualSha = Hash(fullStage);
        if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA256 скачанного EXE не совпадает.");

        Directory.CreateDirectory(BackupDir);
        string backup = Path.Combine(BackupDir, "uUnityBridge-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
            "-" + Guid.NewGuid().ToString("N")[..8] + ".exe");
        File.Copy(target, backup, overwrite: false);
        var last = new LastGood(backup, Hash(backup), target, DateTime.UtcNow.ToString("O"));
        File.WriteAllText(LastGoodPath, JsonSerializer.Serialize(last));

        string health = Path.Combine(AppData, "updates", "ready-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.Copy(fullStage, target, overwrite: true);
            if (!string.Equals(Hash(target), expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Проверка EXE после замены не прошла.");
            var process = Start(target, health);
            if (!WaitForHealth(process, health))
                throw new IOException("Новая версия не подтвердила успешное открытие окна.");
            Log("UPDATED: " + expectedSha[..12] + "; backup: " + Path.GetFileName(backup));
        }
        catch (Exception e)
        {
            // Restore only after the candidate process is fully stopped.
            foreach (var p in Process.GetProcessesByName("uUnityBridge"))
            {
                try
                {
                    if (string.Equals(p.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase))
                    {
                        p.Kill(entireProcessTree: true);
                        p.WaitForExit(10000);
                    }
                }
                catch { /* Best effort; restore will fail visibly if still locked. */ }
                finally { p.Dispose(); }
            }
            File.Copy(backup, target, overwrite: true);
            if (Hash(target) != last.BackupSha256) throw new IOException("Контроль отката не прошёл.", e);
            BlockVersion(expectedSha);
            Log("ROLLED BACK after failed update: " + e.Message);
            Start(target, null);
        }
        finally { try { if (File.Exists(health)) File.Delete(health); } catch { } }
    }

    private static void Rollback(string target)
    {
        if (!File.Exists(LastGoodPath)) throw new FileNotFoundException("Нет предыдущей версии.");
        var manifest = JsonSerializer.Deserialize<LastGood>(File.ReadAllText(LastGoodPath))
            ?? throw new InvalidDataException("Резервная версия не определена.");
        if (!string.Equals(manifest.TargetPath, target, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(manifest.BackupPath).StartsWith(
                Path.GetFullPath(BackupDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(manifest.BackupPath) || Hash(manifest.BackupPath) != manifest.BackupSha256)
            throw new InvalidDataException("Резервная версия отсутствует либо повреждена.");
        string rolledBackFrom = Hash(target);
        File.Copy(manifest.BackupPath, target, overwrite: true);
        if (Hash(target) != manifest.BackupSha256) throw new IOException("Контрольная сумма отката не совпадает.");
        BlockVersion(rolledBackFrom);
        Log("MANUAL ROLLBACK: " + Path.GetFileName(manifest.BackupPath));
        Start(target, null);
    }

    private static Process Start(string target, string? healthFile)
    {
        var info = new ProcessStartInfo(target)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(target)!
        };
        if (healthFile != null)
        {
            info.ArgumentList.Add("--update-health-file");
            info.ArgumentList.Add(healthFile);
        }
        return Process.Start(info) ?? throw new IOException("Не удалось запустить приложение.");
    }

    private static bool WaitForHealth(Process process, string health)
    {
        try
        {
            for (int i = 0; i < 120; i++)
            {
                if (File.Exists(health) && File.ReadAllText(health).Trim() == "ready" && !process.HasExited)
                    return true;
                if (process.HasExited) return false;
                Thread.Sleep(250);
            }
            return false;
        }
        finally { process.Dispose(); }
    }

    private static void WaitForExit(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.WaitForExit(90000))
                throw new TimeoutException("Приложение не закрылось, обновление отменено.");
        }
        catch (ArgumentException) { /* Already exited. */ }
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static void BlockVersion(string sha)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BlockedShaPath)!);
            File.WriteAllText(BlockedShaPath, sha.ToLowerInvariant());
        }
        catch { /* Failure is logged by the outer updater when necessary. */ }
    }

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(AppData);
            File.WriteAllText(LogPath, DateTimeOffset.UtcNow.ToString("O") + " " + message);
        }
        catch { }
    }
}
