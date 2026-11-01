using System.Security.Cryptography;

namespace uUnityBridge.Updater;

// Production file-switch primitive with injected process operations.
// The caller must validate a locally authorized target and a trusted Release.
// This primitive never calls GitHub, opens a network connection, or chooses a path.
internal static class AgentUpgradeTransaction
{
    internal static string Apply(
        string target, string candidate, string expectedSha, string backupDirectory,
        Action stopExactProcess, Func<bool> launchAndProbeHealth, Action restartPrevious)
    {
        if (Path.GetFileName(target) != "uUnityBridge.Agent.exe" ||
            Path.GetFileName(candidate) != "uUnityBridge.Agent.exe" ||
            !File.Exists(target) || !File.Exists(candidate) ||
            expectedSha.Length != 64 || !expectedSha.All(Uri.IsHexDigit) ||
            !string.Equals(Hash(candidate), expectedSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Missing or invalid verified agent payload.");

        // Never touch the running target if the new bytes cannot first be copied
        // and independently verified in the target directory.
        string prepared = target + ".uub-next";
        if (File.Exists(prepared))
            throw new InvalidDataException("Unexamined previous staged agent.");
        Directory.CreateDirectory(backupDirectory);
        string backup = Path.Combine(backupDirectory,
            "agent-" + Guid.NewGuid().ToString("N") + ".exe");
        File.Copy(target, backup, overwrite: false);
        string originalSha = Hash(backup);
        bool preparedCreated = false;
        bool targetReplaced = false;
        bool stopAttempted = false;
        try
        {
            File.Copy(candidate, prepared, overwrite: false);
            preparedCreated = true;
            if (!string.Equals(Hash(prepared), expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Agent staging hash mismatch.");
            stopAttempted = true;
            stopExactProcess();
            File.Move(prepared, target, overwrite: true);
            preparedCreated = false;
            targetReplaced = true;
            if (!string.Equals(Hash(target), expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Installed agent checksum mismatch.");
            if (!launchAndProbeHealth())
                throw new IOException("Updated agent IPC health check failed.");
            return backup;
        }
        catch
        {
            if (targetReplaced)
            {
                // Health callback may have started the candidate, so stop only
                // the exact target again before restoring the old version.
                stopExactProcess();
                File.Copy(backup, target, overwrite: true);
                if (!string.Equals(Hash(target), originalSha, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Rollback checksum mismatch; backup retained.");
            }
            // Even on a failed stop, try to restore service from the unmodified
            // old target. Caller handles duplicate-process protection.
            if (stopAttempted)
            {
                try { restartPrevious(); }
                catch { /* Preserve primary exception; backup remains intact. */ }
            }
            throw;
        }
        finally
        {
            // The candidate temp file is ours only when this attempt created it.
            if (preparedCreated && File.Exists(prepared))
                File.Delete(prepared);
        }
    }

    internal static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
