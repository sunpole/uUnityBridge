using System.Text;
using System.Diagnostics;
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
            lines = lines.Where(IsIssueLine);
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

    internal static bool HeaderMatchesApprovedProject(string header, string project)
    {
        string normalized = header.Replace('\\', '/');
        string approved = Path.GetFullPath(project).Replace('\\', '/');
        int start = normalized.IndexOf(approved, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return false;
        int end = start + approved.Length;
        // A different folder like uUnityDemoBackups must not qualify.
        return end == normalized.Length ||
            char.IsWhiteSpace(normalized[end]) ||
            normalized[end] is '\'' or '"' or ',' or ';';
    }

    // Readability and provenance are separate. Never call blocked logs "no errors".
    internal sealed record EditorResult(string Status, string[] Lines);
    internal sealed record UnityProcess(string Title, string Executable, DateTimeOffset StartedUtc);

    internal static bool IsIssueLine(string line)
    {
        return line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("compilation", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("has no meta file", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("immutable folder", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("asset will be ignored", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsApprovedEditorProcess(UnityProcess process, string project)
    {
        string expectedProject = Path.GetFileName(Path.GetFullPath(project)
            .TrimEnd(Path.DirectorySeparatorChar));
        return expectedProject.Equals("uUnityDemo", StringComparison.OrdinalIgnoreCase) &&
            process.Title.Contains(expectedProject + " - ", StringComparison.OrdinalIgnoreCase) &&
            process.Executable.EndsWith(
                @"\Editor\Unity.exe", StringComparison.OrdinalIgnoreCase) &&
            process.Executable.Contains("6000.6.7f1", StringComparison.OrdinalIgnoreCase);
    }

    internal static string ValidateLogSource(
        string header, string project, IReadOnlyList<UnityProcess> running,
        DateTimeOffset lastWriteUtc)
    {
        if (HeaderMatchesApprovedProject(header, project))
            return "VERIFIED_EDITOR_LOG_PROJECT_HEADER";
        // Editor.log does not always include the project path in its first 32KB.
        // Fallback only if one Unity Editor process exists on the machine, the
        // expected window is open, and the log was modified during that run.
        if (running.Count != 1 || !IsApprovedEditorProcess(running[0], project))
            return "BLOCKED_EDITOR_IDENTITY";
        if (lastWriteUtc < running[0].StartedUtc.AddMinutes(-1) ||
            DateTimeOffset.UtcNow - lastWriteUtc > TimeSpan.FromMinutes(10))
            return "BLOCKED_EDITOR_LOG_STALE";
        return "VERIFIED_EXCLUSIVE_RUNNING_EDITOR";
    }

    private static List<UnityProcess> RunningEditors()
    {
        var running = new List<UnityProcess>();
        foreach (var process in Process.GetProcessesByName("Unity"))
        {
            using (process)
            {
                try
                {
                    string path = process.MainModule?.FileName ?? "";
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        // Cannot determine the source -> force blocked via a
                        // non-matching synthetic record, not silently discard.
                        running.Add(new UnityProcess("", "", DateTimeOffset.MinValue));
                        continue;
                    }
                    running.Add(new UnityProcess(process.MainWindowTitle, path,
                        process.StartTime.ToUniversalTime()));
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or
                    InvalidOperationException or UnauthorizedAccessException)
                {
                    running.Add(new UnityProcess("", "", DateTimeOffset.MinValue));
                }
            }
        }
        return running;
    }

    // Windows Unity Editor log, best-effort rather than a guaranteed live
    // Editor Console subscription. No logs leave the local user session.
    internal static EditorResult ReadEditorErrors(string approvedProject)
    {
        string location = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Unity", "Editor", "Editor.log");
        try
        {
            if (!File.Exists(location) ||
                File.GetAttributes(location).HasFlag(FileAttributes.ReparsePoint))
                return new("BLOCKED_EDITOR_LOG_MISSING_OR_LINK", []);
            var stamp = File.GetLastWriteTimeUtc(location);
            using var stream = new FileStream(location, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int prefixLength = (int)Math.Min(32 * 1024, stream.Length);
            var prefix = new byte[prefixLength];
            int got = stream.Read(prefix, 0, prefix.Length);
            string header = Encoding.UTF8.GetString(prefix, 0, got);
            string source = ValidateLogSource(
                header, approvedProject, RunningEditors(), stamp);
            if (!source.StartsWith("VERIFIED_", StringComparison.Ordinal))
                return new(source, []);
            long count = Math.Min(stream.Length, MaxEditorTailBytes);
            stream.Seek(-count, SeekOrigin.End);
            var buffer = new byte[(int)count];
            int length = stream.Read(buffer, 0, length: buffer.Length);
            string[] lines = Excerpt(
                Encoding.UTF8.GetString(buffer, 0, length), errorsOnly: true);
            return new(source, lines);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            return new("BLOCKED_EDITOR_LOG_UNREADABLE", []);
        }
    }
}
