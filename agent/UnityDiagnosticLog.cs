using System.Text;
using System.Text.RegularExpressions;

namespace uUnityBridge.Agent;

// Only fixed local Unity sources. Never send raw logs, screenshots,
// filesystem paths, auth parameters or tokens to public GitHub Issues.
internal static class UnityDiagnosticLog
{
    private const int MaxLines = 10;
    private const int MaxLineCharacters = 180;
    private const int MaxEditorTailBytes = 48 * 1024;
    private static readonly Regex Secret = new(
        @"(?i)(?:\bBearer\s+\S+|\bgh[pousr]_[A-Za-z0-9_]+|\bgithub_pat_[A-Za-z0-9_]+|\beyJ[A-Za-z0-9_\-]{16,}\.[A-Za-z0-9_\-.]+|\b(?:token|secret|password|api[_-]?key)\s*[:=]\s*\S+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Email = new(
        @"(?i)\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Url = new(
        @"(?i)\b(?:https?|file)://\S+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WinPath = new(
        @"(?i)(?:[a-z]:\\|\\\\)[^\r\n]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UnixPath = new(
        @"(?i)/(?:Users|home|mnt|tmp|var|etc)/[^\s]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static string[] Excerpt(string? data, bool errorsOnly)
    {
        if (string.IsNullOrEmpty(data)) return [];
        IEnumerable<string> lines = data.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().TrimEnd('\r'));
        if (errorsOnly)
            lines = lines.Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("compilation", StringComparison.OrdinalIgnoreCase));
        return lines.TakeLast(MaxLines).Select(Sanitize).Where(x => x.Length != 0).ToArray();
    }

    internal static string Sanitize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        string line = input.Replace("\r", "").Replace("\n", " ").Replace("\t", " ");
        line = Secret.Replace(line, "[secret]");
        line = Url.Replace(line, "[url]");
        line = Email.Replace(line, "[email]");
        line = WinPath.Replace(line, "[local path]");
        line = UnixPath.Replace(line, "[local path]");
        line = new string(line.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return line.Length <= MaxLineCharacters ? line : line[..MaxLineCharacters] + "…";
    }

    // Unity Editor writes its Console to a known per-user Editor.log on Windows.
    // This is a best-effort excerpt, not a guaranteed live Console subscription.
    internal static string[] ReadEditorErrors(string approvedProject)
    {
        string location = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Unity", "Editor", "Editor.log");
        try
        {
            if (!File.Exists(location) ||
                File.GetAttributes(location).HasFlag(FileAttributes.ReparsePoint))
                return [];
            using var stream = new FileStream(location, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            // Unity Editor.log is global per Windows user: never disclose
            // its contents if the log cannot be tied to the approved project.
            int prefixLength = (int)Math.Min(32 * 1024, stream.Length);
            var prefix = new byte[prefixLength];
            int got = stream.Read(prefix, 0, prefix.Length);
            string header = Encoding.UTF8.GetString(prefix, 0, got)
                .Replace('\\', '/');
            string approved = Path.GetFullPath(approvedProject).Replace('\\', '/');
            if (!header.Contains(approved, StringComparison.OrdinalIgnoreCase))
                return [];
            long count = Math.Min(stream.Length, MaxEditorTailBytes);
            stream.Seek(-count, SeekOrigin.End);
            var buffer = new byte[(int)count];
            int length = stream.Read(buffer, 0, buffer.Length);
            return Excerpt(Encoding.UTF8.GetString(buffer, 0, length), errorsOnly: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            return [];
        }
    }
}
