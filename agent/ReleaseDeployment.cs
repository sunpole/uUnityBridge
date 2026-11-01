using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace uUnityBridge.Agent;

// A GitHub Issue can select a PRE-APPROVED local profile, never a path, URL or command.
internal sealed record DeployProfile(
    string Id, string Repository, string AssetName, string Entrypoint,
    string InstallRoot, string WorkflowFile, bool Enabled);
internal sealed record DeploySettings(string Schema, List<DeployProfile> Profiles);
internal sealed record DeploymentResult(string Status, string Code);
internal interface IDeploymentExecutor
{
    Task<DeploymentResult> ExecuteAsync(RelayTask task, CancellationToken ct);
}
internal sealed record DeploymentState(
    string Schema, string Version, string Sha256, string JobId);

internal sealed class ReleaseDeployment : IDeploymentExecutor
{
    internal const string SettingsSchema = "uub.deploy.settings.v1";
    private const string StateSchema = "uub.deploy.state.v1";
    private const long MaxArchiveBytes = 1_073_741_824L;
    private const long MaxExtractBytes = 2_147_483_648L;
    private const int MaxFiles = 4096;
    private static readonly Regex RepoPattern = new(
        @"^sunpole/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant);
    private static readonly Regex AssetPattern = new(
        @"^[A-Za-z0-9_.-]+\.zip$", RegexOptions.CultureInvariant);
    private static readonly Regex NamePattern = new(
        @"^[A-Za-z0-9_. -]+$", RegexOptions.CultureInvariant);
    private static readonly Regex ShaPattern = new(
        @"^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "uUnityBridge", "deployment-profiles.json");

    public async Task<DeploymentResult> ExecuteAsync(RelayTask task, CancellationToken ct)
    {
        try
        {
            return await ExecuteCheckedAsync(task, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException
            or JsonException or UnauthorizedAccessException or InvalidOperationException
            or OperationCanceledException or System.ComponentModel.Win32Exception
            or KeyNotFoundException or ArgumentException or FormatException
            or AbandonedMutexException)
        {
            // No paths, user data, stdout/stderr or token material in public Issues.
            return new DeploymentResult("FAIL", "verification_or_install_failed");
        }
    }

    private static async Task<DeploymentResult> ExecuteCheckedAsync(
        RelayTask job, CancellationToken ct)
    {
        if (job.Action != "install_release" || job.Profile is null ||
            job.ReleaseTag is null || job.Sha256 is null ||
            job.ReleaseId < 1 || job.AssetId < 1 || job.RunId < 1)
            return new DeploymentResult("BLOCKED", "invalid_job");
        if (!File.Exists(SettingsPath))
            return new DeploymentResult("BLOCKED", "local_approval_missing");
        var settings = JsonSerializer.Deserialize<DeploySettings>(
            await File.ReadAllTextAsync(SettingsPath, ct), JsonOptions);
        if (settings is null || settings.Schema != SettingsSchema ||
            settings.Profiles is null || settings.Profiles.Count > 20)
            return new DeploymentResult("BLOCKED", "invalid_local_approval");

        var profiles = settings.Profiles.Where(x => x.Id == job.Profile).ToList();
        if (profiles.Count != 1 || !profiles[0].Enabled)
            return new DeploymentResult("BLOCKED", "profile_not_approved");
        var profile = profiles[0];
        ValidateProfile(profile);
        string root = Path.GetFullPath(profile.InstallRoot);
        if (!Directory.Exists(root) || !Path.IsPathFullyQualified(root))
            return new DeploymentResult("BLOCKED", "approved_folder_missing");
        EnsureNoReparseAncestors(root);
        if (string.Equals(root.TrimEnd(Path.DirectorySeparatorChar),
            Path.GetPathRoot(root)!.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase))
            return new DeploymentResult("BLOCKED", "unsafe_install_root");

        // A process-local mutex exists already (Program.Main). This one also
        // prevents multiple independently launched agent processes from deploying.
        using var mutex = new Mutex(false, @"Local\uUnityBridge.Deploy.v1");
        if (!mutex.WaitOne(TimeSpan.FromSeconds(1)))
            return new DeploymentResult("BLOCKED", "deployment_busy");
        try
        {
            string statePath = Path.Combine(root, "state.json");
            string current = Path.Combine(root, "current");
            var previous = ReadPrevious(statePath, current);
            if (previous is not null)
            {
                if (previous.JobId == job.Id && previous.Sha256 == job.Sha256)
                    return new DeploymentResult("PASS", "already_installed");
                var oldVersion = Version.Parse(previous.Version[1..]);
                var newVersion = Version.Parse(job.ReleaseTag[1..]);
                if (newVersion <= oldVersion)
                    return new DeploymentResult("BLOCKED", "non_increasing_version");
            }
            else if (Directory.Exists(current))
                return new DeploymentResult("BLOCKED", "unmanaged_existing_install");

            string metadata = await RunGhAsync(["api",
                "repos/" + profile.Repository + "/releases/tags/" + job.ReleaseTag], 20, ct);
            using var releaseDoc = JsonDocument.Parse(metadata);
            var release = releaseDoc.RootElement;
            if (release.GetProperty("id").GetInt64() != job.ReleaseId ||
                release.GetProperty("tag_name").GetString() != job.ReleaseTag ||
                release.GetProperty("draft").GetBoolean() ||
                release.GetProperty("prerelease").GetBoolean())
                return new DeploymentResult("BLOCKED", "unapproved_release");
            var assets = release.GetProperty("assets").EnumerateArray()
                .Where(x => x.GetProperty("name").GetString() == profile.AssetName).ToArray();
            if (assets.Length != 1 || assets[0].GetProperty("id").GetInt64() != job.AssetId ||
                assets[0].GetProperty("size").GetInt64() is < 1024 or > MaxArchiveBytes)
                return new DeploymentResult("BLOCKED", "asset_identity_mismatch");
            if (assets[0].TryGetProperty("digest", out var digest) &&
                digest.ValueKind == JsonValueKind.String &&
                digest.GetString() is string assetDigest &&
                !string.Equals(assetDigest, "sha256:" + job.Sha256, StringComparison.OrdinalIgnoreCase))
                return new DeploymentResult("BLOCKED", "asset_digest_mismatch");

            // Ensure a successful CI build corresponds to the commit referenced by the tag.
            string taggedSha = (await RunGhAsync(["api", "repos/" + profile.Repository +
                "/commits/" + job.ReleaseTag, "--jq", ".sha"], 20, ct)).Trim();
            string runJson = await RunGhAsync(["api", "repos/" + profile.Repository +
                "/actions/runs/" + job.RunId], 20, ct);
            using var runDoc = JsonDocument.Parse(runJson);
            var run = runDoc.RootElement;
            if (run.GetProperty("id").GetInt64() != job.RunId ||
                run.GetProperty("conclusion").GetString() != "success" ||
                run.GetProperty("head_sha").GetString() != taggedSha ||
                run.GetProperty("head_branch").GetString() != "main" ||
                run.GetProperty("event").GetString() is not ("push" or "workflow_dispatch") ||
                run.GetProperty("repository").GetProperty("full_name").GetString() != profile.Repository ||
                !(run.GetProperty("path").GetString() ?? "").StartsWith(
                    ".github/workflows/" + profile.WorkflowFile + "@", StringComparison.Ordinal))
                return new DeploymentResult("BLOCKED", "ci_provenance_failed");

            string stage = Path.Combine(root, ".stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                _ = await RunGhAsync(["release", "download", job.ReleaseTag,
                    "-R", profile.Repository, "--pattern", profile.AssetName,
                    "--dir", stage], 360, ct);
                string archive = Path.Combine(stage, profile.AssetName);
                if (!File.Exists(archive) || new FileInfo(archive).Length > MaxArchiveBytes ||
                    !string.Equals(HashFile(archive), job.Sha256, StringComparison.Ordinal))
                    return new DeploymentResult("FAIL", "download_checksum_failed");
                string extracted = Path.Combine(stage, "unpacked");
                Directory.CreateDirectory(extracted);
                ExtractSafe(archive, extracted);
                if (!File.Exists(Path.Combine(extracted, profile.Entrypoint)))
                    return new DeploymentResult("BLOCKED", "entrypoint_missing");
                await File.WriteAllTextAsync(Path.Combine(extracted, ".uub-managed.json"),
                    JsonSerializer.Serialize(new { schema = StateSchema, profile = profile.Id }), ct);
                EnsureNoReparseAncestors(current, stopAt: root);

                string backup = "";
                if (previous is not null)
                {
                    string backups = Path.Combine(root, "backups");
                    if (Directory.Exists(backups)) EnsureNoReparseAncestors(backups, stopAt: root);
                    Directory.CreateDirectory(backups);
                    backup = Path.Combine(backups, previous.Version + "-" +
                        Guid.NewGuid().ToString("N"));
                }

                bool oldMoved = false;
                bool newMoved = false;
                try
                {
                    if (previous is not null)
                    {
                        Directory.Move(current, backup);
                        oldMoved = true;
                    }
                    Directory.Move(extracted, current);
                    newMoved = true;
                    await File.WriteAllTextAsync(statePath + ".next",
                        JsonSerializer.Serialize(new DeploymentState(
                            StateSchema, job.ReleaseTag, job.Sha256, job.Id)), ct);
                    File.Move(statePath + ".next", statePath, overwrite: true);
                }
                catch
                {
                    if (newMoved && Directory.Exists(current))
                    {
                        // Keep failed bytes for diagnosis, without deleting any user file.
                        Directory.Move(current, Path.Combine(root,
                            ".failed-" + Guid.NewGuid().ToString("N")));
                    }
                    if (oldMoved && Directory.Exists(backup))
                        Directory.Move(backup, current);
                    throw;
                }
                return new DeploymentResult("PASS", "installed_files_verified");
            }
            finally
            {
                if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            }
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static DeploymentState? ReadPrevious(string statePath, string current)
    {
        if (!File.Exists(statePath)) return null;
        var old = JsonSerializer.Deserialize<DeploymentState>(
            File.ReadAllText(statePath), JsonOptions);
        if (old is null || old.Schema != StateSchema ||
            !Regex.IsMatch(old.Version ?? "", @"^v\d+\.\d+\.\d+$") ||
            !ShaPattern.IsMatch(old.Sha256 ?? "") ||
            !Regex.IsMatch(old.JobId ?? "", @"^R[0-9]{6}$") ||
            !Directory.Exists(current))
            throw new InvalidDataException("Invalid previous installation.");
        EnsureNoReparseAncestors(current);
        string managed = Path.Combine(current, ".uub-managed.json");
        if (!File.Exists(managed) ||
            !File.ReadAllText(managed).Contains(StateSchema, StringComparison.Ordinal))
            throw new InvalidDataException("Previous install lacks agent marker.");
        return old;
    }

    private static void ValidateProfile(DeployProfile p)
    {
        if (!Regex.IsMatch(p.Id ?? "", @"^[a-z][a-z0-9-]{1,31}$") ||
            !RepoPattern.IsMatch(p.Repository ?? "") ||
            !AssetPattern.IsMatch(p.AssetName ?? "") ||
            !NamePattern.IsMatch(p.Entrypoint ?? "") ||
            !p.Entrypoint.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(p.WorkflowFile ?? "", @"^[A-Za-z0-9_.-]+\.yml$") ||
            string.IsNullOrWhiteSpace(p.InstallRoot) ||
            !Path.IsPathFullyQualified(p.InstallRoot) ||
            p.InstallRoot.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid local deployment profile.");
    }

    internal static void ValidateArchivePath(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 240 ||
            name.StartsWith('/') || name.Contains('\\') || name.Contains(':') ||
            name.Contains("//") || name.Any(char.IsControl))
            throw new InvalidDataException("Unsafe archive path.");
        foreach (string segment in name.TrimEnd('/').Split('/'))
        {
            if (segment is "" or "." or ".." ||
                segment.StartsWith(".uub-", StringComparison.OrdinalIgnoreCase) ||
                segment.EndsWith('.') || segment.EndsWith(' ') ||
                !NamePattern.IsMatch(segment))
                throw new InvalidDataException("Unsafe archive filename.");
            string device = segment.Split('.')[0].TrimEnd(' ');
            if (device.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                device.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                device.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                device.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(device, @"^(COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase))
                throw new InvalidDataException("Device filename in archive.");
        }
    }

    internal static void ExtractSafe(string zipPath, string destination)
    {
        EnsureNoReparseAncestors(destination);
        string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        long total = 0;
        int count = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var zip = ZipFile.OpenRead(zipPath);
        // Validate the ENTIRE central directory before writing the first file.
        foreach (var entry in zip.Entries)
        {
            ValidateArchivePath(entry.FullName);
            string normalized = entry.FullName.TrimEnd('/');
            if (!seen.Add(normalized)) throw new InvalidDataException("Archive name collision.");
            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType is not (0 or 0x8000 or 0x4000))
                throw new InvalidDataException("Archive contains symlink or special file.");
            if (!entry.FullName.EndsWith('/'))
            {
                count++;
                if (count > MaxFiles || entry.Length > MaxExtractBytes - total ||
                    entry.Length < 0 || (entry.CompressedLength > 0 &&
                    entry.Length > entry.CompressedLength * 500))
                    throw new InvalidDataException("Archive size limits exceeded.");
                total += entry.Length;
            }
        }
        if (count == 0) throw new InvalidDataException("Empty archive.");
        foreach (var entry in zip.Entries)
        {
            string output = Path.GetFullPath(Path.Combine(destination,
                entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!output.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Archive path escaped staging.");
            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(output);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var input = entry.Open();
            using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write,
                FileShare.None);
            input.CopyTo(file);
            if (file.Length != entry.Length)
                throw new InvalidDataException("Uncompressed size mismatch.");
        }
    }

    private static void EnsureNoReparseAncestors(string path, string? stopAt = null)
    {
        string full = Path.GetFullPath(path);
        string? boundary = stopAt is null ? null : Path.GetFullPath(stopAt);
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            if (File.Exists(current) || Directory.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked directory/file is forbidden.");
            }
            if (boundary is not null && string.Equals(current, boundary,
                StringComparison.OrdinalIgnoreCase)) break;
            if (Path.GetPathRoot(current) == current) break;
        }
    }

    private static string HashFile(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    // Only fixed gh verbs, structured validated arguments; no shell, scripts, or arbitrary URLs.
    private static async Task<string> RunGhAsync(
        string[] args, int seconds, CancellationToken ct)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo("gh")
        {
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string arg in args) proc.StartInfo.ArgumentList.Add(arg);
        proc.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        Task<string> output = proc.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> errors = proc.StandardError.ReadToEndAsync(timeout.Token);
        try { await proc.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
            throw;
        }
        string value = await output;
        _ = await errors; // Do not expose gh stdout/stderr in GitHub Issues.
        if (proc.ExitCode != 0 || value.Length > 1_000_000)
            throw new InvalidOperationException("GitHub API/download failed.");
        return value.Trim();
    }
}
