using uUnityBridge.Updater;

if (!OperatingSystem.IsWindows() ||
    Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
    throw new InvalidOperationException("P0010 upgrade QA runs only on isolated Windows GitHub Actions.");

string root = Path.Combine(
    Environment.GetEnvironmentVariable("RUNNER_TEMP") ??
    throw new InvalidOperationException("CI temp directory missing."),
    "P0010-upgrade-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;

void Assert(bool condition, string description)
{
    checks++;
    if (!condition) throw new Exception("FAIL: " + description);
    Console.WriteLine("PASS: " + description);
}

void ExpectFailure(Action action, string description)
{
    bool rejected = false;
    try { action(); }
    catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
    {
        rejected = true;
    }
    Assert(rejected, description);
}

(string target, string candidate, string backups, string oldHash, string newHash) Fixture(string name)
{
    string folder = Path.Combine(root, name);
    string install = Path.Combine(folder, "install");
    string incoming = Path.Combine(folder, "incoming");
    string backup = Path.Combine(folder, "backups");
    Directory.CreateDirectory(install);
    Directory.CreateDirectory(incoming);
    string target = Path.Combine(install, "uUnityBridge.Agent.exe");
    string candidate = Path.Combine(incoming, "uUnityBridge.Agent.exe");
    File.WriteAllText(target, "old agent - approved historical version");
    File.WriteAllText(candidate, "new agent - verified new release");
    return (target, candidate, backup,
        AgentUpgradeTransaction.Hash(target), AgentUpgradeTransaction.Hash(candidate));
}

try
{
    // Network outage: no downloaded file is available. No target or process
    // is touched, and no backup or staging is created in the install folder.
    {
        var f = Fixture("network-missing");
        File.Delete(f.candidate);
        int stopped = 0;
        ExpectFailure(() => AgentUpgradeTransaction.Apply(
            f.target, f.candidate, f.newHash, f.backups,
            () => stopped++, () => true, () => { }),
            "network unavailable / missing payload rejected");
        Assert(stopped == 0 && AgentUpgradeTransaction.Hash(f.target) == f.oldHash &&
            !Directory.Exists(f.backups), "missing payload preserves old agent before stop");
    }

    // A truncated network download is not a usable Release asset.
    {
        var f = Fixture("network-truncated");
        using (var bytes = new FileStream(f.candidate, FileMode.Open, FileAccess.Write))
            bytes.SetLength(3);
        int stopped = 0;
        ExpectFailure(() => AgentUpgradeTransaction.Apply(
            f.target, f.candidate, f.newHash, f.backups,
            () => stopped++, () => true, () => { }),
            "partial network payload fails SHA-256");
        Assert(stopped == 0 && AgentUpgradeTransaction.Hash(f.target) == f.oldHash,
            "truncated download never stops or replaces old agent");
    }

    // Integrity error: an altered candidate cannot reach the swap phase.
    {
        var f = Fixture("bad-hash");
        int stopped = 0;
        ExpectFailure(() => AgentUpgradeTransaction.Apply(
            f.target, f.candidate, new string('0', 64), f.backups,
            () => stopped++, () => true, () => { }),
            "mismatched published SHA-256 rejected");
        Assert(stopped == 0 && AgentUpgradeTransaction.Hash(f.target) == f.oldHash,
            "bad SHA cannot change existing EXE");
    }

    // Interrupted old upgrade: never overwrite an unknown .uub-next file.
    {
        var f = Fixture("unknown-staging");
        File.WriteAllText(f.target + ".uub-next", "unknown bytes");
        int stopped = 0;
        ExpectFailure(() => AgentUpgradeTransaction.Apply(
            f.target, f.candidate, f.newHash, f.backups,
            () => stopped++, () => true, () => { }),
            "unexpected pre-existing staging blocked");
        Assert(stopped == 0 && AgentUpgradeTransaction.Hash(f.target) == f.oldHash,
            "pre-existing staging unchanged and old agent intact");
    }

    // The lock may already be present BEFORE backup. In that case copying
    // the old bytes must fail closed, without even attempting to stop it.
    {
        var f = Fixture("locked-before-backup");
        int stopped = 0, restarted = 0, checkedHealth = 0;
        using (var locked = new FileStream(
            f.target, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ExpectFailure(() => AgentUpgradeTransaction.Apply(
                f.target, f.candidate, f.newHash, f.backups,
                () => stopped++, () => { checkedHealth++; return true; },
                () => restarted++),
                "Windows exclusive lock blocks creation of backup");
            Assert(stopped == 0 && checkedHealth == 0 && restarted == 0,
                "preexisting lock is refused before any process action");
        }
        Assert(AgentUpgradeTransaction.Hash(f.target) == f.oldHash &&
            !Directory.EnumerateFiles(f.backups, "*.exe").Any(),
            "locked-before-backup preserves previous bytes and no partial backup");
    }

    // A lock can also arise AFTER verified backup, when a process does not
    // release the target at stop time. The move must fail without corruption.
    {
        var f = Fixture("locked-after-backup");
        int stopped = 0, restarted = 0, checkedHealth = 0;
        FileStream? delayedLock = null;
        try
        {
            ExpectFailure(() => AgentUpgradeTransaction.Apply(
                f.target, f.candidate, f.newHash, f.backups,
                () =>
                {
                    stopped++;
                    delayedLock = new FileStream(f.target,
                        FileMode.Open, FileAccess.Read, FileShare.None);
                },
                () => { checkedHealth++; return true; },
                () => restarted++),
                "Windows locked target rejects switch after backup");
            Assert(stopped == 1 && checkedHealth == 0 && restarted == 1,
                "late lock skips launch and triggers fallback");
        }
        finally { delayedLock?.Dispose(); }
        Assert(AgentUpgradeTransaction.Hash(f.target) == f.oldHash &&
            Directory.EnumerateFiles(f.backups, "*.exe").Any() &&
            !File.Exists(f.target + ".uub-next"),
            "late lock preserves old hash, retained backup and cleaned staging");
    }

    // Simulate IPC health responding FAIL after a successful swap. Restore
    // old agent and restart it; candidate bytes may not remain installed.
    {
        var f = Fixture("health-fail");
        int stopCount = 0, healthCount = 0, restarts = 0;
        ExpectFailure(() => AgentUpgradeTransaction.Apply(
            f.target, f.candidate, f.newHash, f.backups,
            () => stopCount++, () => { healthCount++; return false; },
            () => restarts++),
            "failed local IPC health triggers rollback");
        Assert(stopCount == 2 && healthCount == 1 && restarts == 1 &&
            AgentUpgradeTransaction.Hash(f.target) == f.oldHash,
            "health failure restores old bytes and requests old agent restart");
        Assert(Directory.EnumerateFiles(f.backups, "*.exe")
            .Any(p => AgentUpgradeTransaction.Hash(p) == f.oldHash),
            "healthy previous release retained in backup");
    }

    // Health process can also throw; its exception must not leave a new agent.
    {
        var f = Fixture("health-exception");
        int restarts = 0;
        ExpectFailure(() => AgentUpgradeTransaction.Apply(
            f.target, f.candidate, f.newHash, f.backups,
            () => { }, () => throw new IOException("Simulated Named Pipe failure"),
            () => restarts++),
            "local IPC connection exception triggers rollback");
        Assert(AgentUpgradeTransaction.Hash(f.target) == f.oldHash && restarts == 1,
            "IPC exception restores old EXE");
    }

    // The stop phase can fail (different integrity level/locked process).
    {
        var f = Fixture("stop-refused");
        ExpectFailure(() => AgentUpgradeTransaction.Apply(
            f.target, f.candidate, f.newHash, f.backups,
            () => throw new IOException("Simulated process access denied"),
            () => true, () => { }),
            "unavailable exact-process stop blocks update");
        Assert(AgentUpgradeTransaction.Hash(f.target) == f.oldHash,
            "stop failure does not replace old agent");
    }

    {
        var f = Fixture("happy-path");
        int stopCount = 0, healthCount = 0, restarts = 0;
        string backup = AgentUpgradeTransaction.Apply(
            f.target, f.candidate, f.newHash, f.backups,
            () => stopCount++, () => { healthCount++; return true; },
            () => restarts++);
        Assert(AgentUpgradeTransaction.Hash(f.target) == f.newHash &&
            AgentUpgradeTransaction.Hash(backup) == f.oldHash,
            "verified update installs candidate and retains rollback copy");
        Assert(stopCount == 1 && healthCount == 1 && restarts == 0,
            "healthy update completes without fallback restart");
    }

    Console.WriteLine($"PASS: {checks} isolated P0010 Windows upgrade assertions. " +
        "Only runner temporary fixtures were accessed.");
}
finally
{
    Directory.Delete(root, recursive: true);
}
