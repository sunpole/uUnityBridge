using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace uUnityBridge;

/// <summary>Pure validation and diff planner for future modular distributions.
/// This component performs NO downloading, process execution, or installation.
/// A manifest still requires independent authenticity verification before use.</summary>
internal sealed record PackageEntry(string Path, long Bytes, string Sha256);
internal sealed record PackageManifest(
    string Schema, string Version, string Profile, string Rid, List<PackageEntry> Files);

internal static class PackageFiles
{
    internal const string Schema = "uub.files.v1";
    internal const int MaxFiles = 2048;
    internal const long MaxTotalBytes = 2_147_483_648L;
    private static readonly Regex VersionPattern = new(@"^[0-9]+\.[0-9]+\.[0-9]+$", RegexOptions.CultureInvariant);
    private static readonly Regex SegmentPattern = new(@"^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant);
    private static readonly Regex HashPattern = new(@"^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    // Windows also treats these names as devices when followed by an extension.
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CLOCK$", "COM1", "COM2", "COM3",
        "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1",
        "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    internal static PackageManifest Parse(string json)
    {
        var m = JsonSerializer.Deserialize<PackageManifest>(json)
            ?? throw new InvalidDataException("Package manifest is empty.");
        Validate(m);
        return m;
    }

    internal static void Validate(PackageManifest manifest)
    {
        if (manifest.Schema != Schema || manifest.Rid != "win-x64" ||
            manifest.Profile is not ("compact" or "portable") ||
            !VersionPattern.IsMatch(manifest.Version ?? "") ||
            manifest.Files is not { Count: >= 1 and <= MaxFiles })
            throw new InvalidDataException("Unsupported or empty package manifest.");

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var f in manifest.Files)
        {
            ValidateRelativePath(f.Path);
            if (!paths.Add(f.Path))
                throw new InvalidDataException("Duplicate or case-colliding filename.");
            if (f.Bytes < 0 || f.Bytes > MaxTotalBytes - total)
                throw new InvalidDataException("File length or total package size exceeds limit.");
            total += f.Bytes;
            if (!HashPattern.IsMatch(f.Sha256 ?? ""))
                throw new InvalidDataException("Expected SHA256 must be 64 lowercase hex digits.");
        }
        if (!paths.Contains("uUnityBridge.exe"))
            throw new InvalidDataException("Application entrypoint is missing from the manifest.");
    }

    internal static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 240 || path[0] == '/' ||
            path.Contains('\\') || path.Contains(':') || path.Contains('%') ||
            path.Contains("//") || path.Any(char.IsControl))
            throw new InvalidDataException("Invalid package-relative path.");

        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." ||
                segment.EndsWith('.') || !SegmentPattern.IsMatch(segment) ||
                Reserved.Contains(segment.Split('.')[0]))
                throw new InvalidDataException("Invalid or reserved path segment.");
        }
    }

    // Caller must first authenticate the manifest. No mutable directory is trusted as a
    // source of executable bytes without a separate SHA256 check.
    internal static IReadOnlyList<PackageEntry> MissingOrChanged(
        PackageManifest current, PackageManifest desired)
    {
        Validate(current);
        Validate(desired);
        if (current.Rid != desired.Rid || current.Profile != desired.Profile)
            throw new InvalidDataException("A runtime profile migration requires a separate bootstrap.");
        var existing = current.Files.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        return desired.Files.Where(f =>
            !existing.TryGetValue(f.Path, out var old) ||
            old.Bytes != f.Bytes ||
            !string.Equals(old.Sha256, f.Sha256, StringComparison.Ordinal)).ToArray();
    }

    // Called only by trusted CI to create a manifest of its own publish output.
    internal static PackageManifest CreateFromDirectory(
        string directory, string version, string profile)
    {
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        var root = Path.GetFullPath(directory);
        if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("The package root cannot be a link.");
        var entries = new List<PackageEntry>();
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            ValidateRelativePath(relative);
            string ancestor = Path.GetDirectoryName(path)!;
            while (ancestor.Length >= root.Length && !string.Equals(ancestor, root, StringComparison.OrdinalIgnoreCase))
            {
                if (File.GetAttributes(ancestor).HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidDataException("Linked directories are not allowed.");
                ancestor = Path.GetDirectoryName(ancestor)!;
            }
            var file = new FileInfo(path);
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Linked files are not allowed.");
            using var stream = file.OpenRead();
            var sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            entries.Add(new PackageEntry(relative, file.Length, sha));
        }
        var manifest = new PackageManifest(Schema, version, profile, "win-x64",
            entries.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList());
        Validate(manifest);
        return manifest;
    }

    // Read-only verifier: never follows links, never overwrites missing/corrupt files.
    internal static IReadOnlyList<string> VerifyDirectory(
        PackageManifest manifest, string directory)
    {
        Validate(manifest);
        var root = Path.GetFullPath(directory);
        if (!Directory.Exists(root) ||
            File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Package root missing or is a link.");
        var mismatches = new List<string>();
        foreach (var item in manifest.Files)
        {
            string full = Path.GetFullPath(Path.Combine(root,
                item.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Package path escaped its directory.");

            string? parent = Path.GetDirectoryName(full);
            bool unsafeParent = false;
            while (parent is not null &&
                   !string.Equals(parent, root, StringComparison.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(parent)) break;
                if (File.GetAttributes(parent).HasFlag(FileAttributes.ReparsePoint))
                {
                    unsafeParent = true;
                    break;
                }
                parent = Path.GetDirectoryName(parent);
            }
            if (unsafeParent || !File.Exists(full) ||
                File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint) ||
                new FileInfo(full).Length != item.Bytes)
            {
                mismatches.Add(item.Path);
                continue;
            }
            using var stream = File.OpenRead(full);
            string sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(sha, item.Sha256, StringComparison.Ordinal))
                mismatches.Add(item.Path);
        }
        return mismatches;
    }
}
