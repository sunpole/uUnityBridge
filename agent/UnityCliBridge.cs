using System.Diagnostics;
using System.Text.Json;

namespace uUnityBridge.Agent;

// Only local consent can select a directory. No GitHub Issue/API request supplies
// a path, process name, template, version, URL or command-line argument.
internal static class UnityCliPolicy
{
    internal const string Schema = "uub.unity.approval.v1";
    internal const string Name = "uUnityDemo";
    internal const string EditorVersion = "6000.6.7f1";
    internal static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "uUnityBridge", "unity-approval.json");

    internal sealed record Approval(string Parent)
    {
        internal string Project => Path.Combine(Parent, Name);
    }

    internal sealed record Plan(string Action, string WorkingDirectory,
        string[] Arguments, bool ChangesFiles);

    internal static Approval ReadApproval()
    {
        string file = SettingsFile;
        if (!File.Exists(file) || new FileInfo(file).Length > 4096)
            throw new InvalidDataException("unity_local_approval_required");
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            root.EnumerateObject().Count() != 5 ||
            root.GetProperty("schema").GetString() != Schema ||
            root.GetProperty("enabled").ValueKind != JsonValueKind.True ||
            root.GetProperty("projectName").GetString() != Name ||
            root.GetProperty("editorVersion").GetString() != EditorVersion)
            throw new InvalidDataException("unity_approval_invalid");
        string parent = root.GetProperty("projectParent").GetString() ?? "";
        ValidateParent(parent);
        return new Approval(Path.GetFullPath(parent));
    }

    internal static void ValidateParent(string parent)
    {
        if (!Path.IsPathFullyQualified(parent) || !Directory.Exists(parent))
            throw new InvalidDataException("unity_parent_missing");
        string full = Path.GetFullPath(parent);
        string volume = Path.GetPathRoot(full) ?? "";
        if (full.TrimEnd(Path.DirectorySeparatorChar).Equals(
            volume.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("unity_drive_root_not_allowed");

        // Reject any link or junction in the path, including an existing project.
        for (string? cursor = full; cursor is not null; cursor = Path.GetDirectoryName(cursor))
        {
            if (File.GetAttributes(cursor).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("unity_symlink_not_allowed");
            if (cursor.Equals(volume, StringComparison.OrdinalIgnoreCase)) break;
        }
        string project = Path.Combine(full, Name);
        if (Directory.Exists(project) &&
            File.GetAttributes(project).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("unity_project_symlink_not_allowed");
    }

    internal static Plan Build(string action, Approval approval)
    {
        ValidateParent(approval.Parent);
        bool exists = Directory.Exists(approval.Project);
        bool projectValid = exists && File.Exists(Path.Combine(
            approval.Project, "ProjectSettings", "ProjectVersion.txt")) &&
            Directory.Exists(Path.Combine(approval.Project, "Assets"));

        return action switch
        {
            "unity_status" => new(action, approval.Parent,
                ["--no-banner", "--format", "json", "status",
                 "--project-path", approval.Project], false),

            "unity_project_create" when !exists => new(action, approval.Parent,
                ["--no-banner", "--non-interactive", "projects", "create",
                 Name, "--path", approval.Project, "--template", "2d",
                 "--editor-version", EditorVersion,
                 "--no-cloud", "--with-pipeline"], true),

            "unity_project_open" when projectValid => new(action, approval.Parent,
                ["--no-banner", "--non-interactive", "projects", "open",
                 approval.Project], true),

            "unity_project_create" => throw new InvalidDataException("unity_project_already_exists"),
            "unity_project_open" => throw new InvalidDataException("unity_project_not_ready"),
            _ => throw new InvalidDataException("unity_unknown_action")
        };
    }
}

// Process execution is never driven by arbitrary caller text. The only process
// is the user's installed Unity CLI, via ArgumentList (not cmd.exe/PowerShell).
internal interface IUnityBridge
{
    Task<object> RunAsync(string action, CancellationToken ct);
}

internal sealed class UnityCliBridge : IUnityBridge
{
    private readonly object gate = new();
    private Task? active;
    private string lastOperation = "none";
    private string lastOutcome = "not_run";
    internal static readonly UnityCliBridge Shared = new();

    public async Task<object> RunAsync(string action, CancellationToken ct)
    {
        try
        {
            var approval = UnityCliPolicy.ReadApproval();
            var plan = UnityCliPolicy.Build(action, approval);

            if (!plan.ChangesFiles)
            {
                using var process = Start(plan, capture: true);
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limit.CancelAfter(TimeSpan.FromSeconds(8));
                Task<string> stdout = process.StandardOutput.ReadToEndAsync(limit.Token);
                Task<string> stderr = process.StandardError.ReadToEndAsync(limit.Token);
                try { await process.WaitForExitAsync(limit.Token); }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited) process.Kill();
                    return new { status = "BLOCKED", action, error = "unity_cli_timeout" };
                }
                string output = await stdout;
                _ = await stderr; // Never return raw CLI logs or local paths.
                bool ready = process.ExitCode == 0 && output.Length <= 32768 &&
                    output.Contains("ready", StringComparison.OrdinalIgnoreCase) &&
                    output.Contains(UnityCliPolicy.Name, StringComparison.OrdinalIgnoreCase);
                string previous;
                string previousOutcome;
                lock (gate) { previous = lastOperation; previousOutcome = lastOutcome; }
                return new
                {
                    status = ready ? "PASS" : "BLOCKED", action,
                    editorReady = ready, lastOperation = previous,
                    lastOperationStatus = previousOutcome,
                    error = ready ? "" : "unity_editor_not_ready"
                };
            }

            lock (gate)
            {
                if (active is { IsCompleted: false })
                    return new { status = "BLOCKED", action, error = "unity_operation_busy" };

                // Launch before acknowledging; completion is NOT claimed.
                var process = Start(plan, capture: false);
                lastOperation = action;
                lastOutcome = "RUNNING";
                active = MonitorAsync(process);
                return new { status = "ACCEPTED", action,
                    completion = "pending", error = "" };
            }
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidDataException or
            UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException or
            KeyNotFoundException or InvalidOperationException)
        {
            return new { status = "BLOCKED", action, error =
                e is InvalidDataException ? e.Message : "unity_cli_unavailable_or_invalid_config" };
        }
    }

    private async Task MonitorAsync(Process process)
    {
        using (process)
        {
            int code = -1;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                await process.WaitForExitAsync(timeout.Token);
                code = process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(); }
                catch (InvalidOperationException) { }
            }
            catch (Exception e) when (e is IOException or InvalidOperationException) { }
            lock (gate) lastOutcome = code == 0 ? "CLI_EXIT_0_VERIFY_IN_UNITY" :
                "CLI_FAILED_OR_TIMED_OUT";
        }
    }

    private static Process Start(UnityCliPolicy.Plan plan, bool capture)
    {
        var start = new ProcessStartInfo("unity")
        {
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = capture,
            RedirectStandardError = capture
        };
        foreach (string argument in plan.Arguments)
            start.ArgumentList.Add(argument);
        // The CLI can otherwise create a cloud project by default.
        start.Environment["UNITY_NO_CLOUD"] = "1";
        start.Environment["UNITY_NO_PAGER"] = "1";
        var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            process.Dispose();
            throw new IOException("unity_cli_not_started");
        }
        return process;
    }
}
