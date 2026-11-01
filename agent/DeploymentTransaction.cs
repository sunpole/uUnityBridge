using System.Text.Json;
using System.Text.RegularExpressions;

namespace uUnityBridge.Agent;

// Narrow, recoverable directory switch. No arbitrary paths are accepted from
// GitHub Issues: the root is a locally approved directory and the candidate
// must already have passed verified ZIP extraction.
internal static class DeploymentTransaction
{
    private const string PendingSchema = "uub.deploy.pending.v1";
    private const string ManagedSchema = "uub.deploy.state.v1";
    private static readonly JsonSerializerOptions Options =
        new() { PropertyNameCaseInsensitive = true };
    private sealed record Pending(string Schema, string JobId, string? BackupName);

    internal static void WriteManagedMarker(
        string candidate, string profile, string jobId)
    {
        File.WriteAllText(Path.Combine(candidate, ".uub-managed.json"),
            JsonSerializer.Serialize(new
            {
                schema = ManagedSchema, profile, jobId
            }));
    }

    internal static void Recover(string root, string profile)
    {
        string journal = Path.Combine(root, ".deploy-pending.json");
        if (!File.Exists(journal)) return;
        EnsureNoLink(journal);
        Pending? pending = JsonSerializer.Deserialize<Pending>(
            File.ReadAllText(journal), Options);
        if (pending is null || pending.Schema != PendingSchema ||
            !Regex.IsMatch(pending.JobId ?? "", @"^R[0-9]{6}$") ||
            (pending.BackupName is not null &&
             !Regex.IsMatch(pending.BackupName, @"^v[0-9]+\.[0-9]+\.[0-9]+-[a-f0-9]{32}$")))
            throw new InvalidDataException("Unrecognized pending deployment.");

        string current = Path.Combine(root, "current");
        string stateFile = Path.Combine(root, "state.json");
        if (File.Exists(stateFile))
        {
            EnsureNoLink(stateFile);
            DeploymentState? state = JsonSerializer.Deserialize<DeploymentState>(
                File.ReadAllText(stateFile), Options);
            if (state?.Schema != ManagedSchema)
                throw new InvalidDataException("Invalid deployment state.");
            if (state.JobId == pending.JobId)
            {
                RequireMarker(current, profile, pending.JobId);
                File.Delete(journal);
                return; // Commit succeeded, only cleanup was interrupted.
            }
        }

        string? backup = pending.BackupName is null ? null :
            Path.Combine(root, "backups", pending.BackupName);
        if (backup is not null && Directory.Exists(backup))
        {
            RequireMarker(backup, profile, null);
            if (Directory.Exists(current))
            {
                RequireMarker(current, profile, pending.JobId);
                Quarantine(root, current);
            }
            Directory.Move(backup, current);
            File.Delete(journal);
            return;
        }

        if (Directory.Exists(current))
        {
            string? jobId = ReadMarkerJobId(current, profile);
            if (jobId == pending.JobId)
            {
                if (backup is not null)
                    throw new InvalidDataException("Previous version backup missing.");
                // Initial installation interrupted before the state was committed.
                Quarantine(root, current);
            }
            else if (backup is not null && jobId is null)
            {
                // Initial marker in an older version may not have a jobId.
                // Previous directory was never moved, so it is still usable.
            }
            else if (backup is not null)
            {
                // Existing current has a different job than the pending one:
                // it is the unmodified old installation.
            }
            else
                throw new InvalidDataException("Unexpected current installation.");
        }
        else if (backup is not null)
        {
            throw new InvalidDataException("Previous version and backup missing.");
        }
        File.Delete(journal);
    }

    internal static void Install(
        string root, string candidate, DeploymentState next,
        string profile, DeploymentState? previous,
        Action<string>? fault = null)
    {
        Recover(root, profile);
        if (!Directory.Exists(candidate))
            throw new InvalidDataException("Missing staged candidate.");
        RequireMarker(candidate, profile, next.JobId);
        string current = Path.Combine(root, "current");
        string stateFile = Path.Combine(root, "state.json");
        string journal = Path.Combine(root, ".deploy-pending.json");
        string? backupName = null;
        if (previous is not null)
        {
            RequireMarker(current, profile, null);
            backupName = previous.Version + "-" + Guid.NewGuid().ToString("N");
            string backups = Path.Combine(root, "backups");
            Directory.CreateDirectory(backups);
            EnsureNoLink(backups);
        }
        else if (Directory.Exists(current) || File.Exists(stateFile))
        {
            throw new InvalidDataException("Unmanaged existing installation.");
        }

        // Journal goes to disk BEFORE the first rename.
        File.WriteAllText(journal + ".next", JsonSerializer.Serialize(
            new Pending(PendingSchema, next.JobId, backupName)));
        File.Move(journal + ".next", journal, overwrite: false);
        try
        {
            if (backupName is not null)
            {
                string backup = Path.Combine(root, "backups", backupName);
                Directory.Move(current, backup);
                fault?.Invoke("old_moved");
            }
            Directory.Move(candidate, current);
            fault?.Invoke("new_moved");
            File.WriteAllText(stateFile + ".next", JsonSerializer.Serialize(next));
            File.Move(stateFile + ".next", stateFile, overwrite: true);
            fault?.Invoke("state_committed");
            File.Delete(journal);
        }
        catch
        {
            Recover(root, profile);
            throw;
        }
    }

    private static void Quarantine(string root, string current)
    {
        // Never delete unknown or user files, even during rollback.
        Directory.Move(current, Path.Combine(root,
            ".failed-" + Guid.NewGuid().ToString("N")));
    }

    private static void EnsureNoLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) &&
            File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Reparse point in deployment transaction.");
    }

    private static string? ReadMarkerJobId(string folder, string profile)
    {
        EnsureNoLink(folder);
        string file = Path.Combine(folder, ".uub-managed.json");
        if (!File.Exists(file)) throw new InvalidDataException("Missing managed marker.");
        EnsureNoLink(file);
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var root = doc.RootElement;
        if (root.GetProperty("schema").GetString() != ManagedSchema ||
            root.GetProperty("profile").GetString() != profile)
            throw new InvalidDataException("Unexpected install marker.");
        return root.TryGetProperty("jobId", out var element)
            ? element.GetString() : null;
    }

    internal static void RequireMarker(string folder, string profile, string? expectedJob)
    {
        if (!Directory.Exists(folder))
            throw new InvalidDataException("Missing managed install directory.");
        string? actual = ReadMarkerJobId(folder, profile);
        if (expectedJob is not null && actual != expectedJob)
            throw new InvalidDataException("Wrong deployment job marker.");
    }
}
