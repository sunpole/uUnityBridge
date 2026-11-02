using uUnityBridge.Agent;

if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
    throw new Exception("Test must run in ephemeral GitHub Actions runner only.");

string root = Path.Combine(
    Environment.GetEnvironmentVariable("RUNNER_TEMP") ??
        throw new Exception("Runner temp missing"),
    "unity-bridge-qa-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int assertions = 0;
void Check(bool ok, string what)
{
    if (!ok) throw new Exception("FAIL: " + what);
    assertions++;
    Console.WriteLine("PASS: " + what);
}
void Deny(Action action, string name)
{
    bool rejected = false;
    try { action(); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, name);
}
try
{
    string parent = Path.Combine(root, "approved");
    Directory.CreateDirectory(parent);
    var profile = new UnityCliPolicy.Approval(parent);
    var create = UnityCliPolicy.Build("unity_project_create", profile);
    Check(create.ChangesFiles &&
        create.WorkingDirectory == parent &&
        create.Arguments.SequenceEqual(new[]
        {
            "--no-banner", "--non-interactive", "projects", "create",
            "uUnityDemo", "--path", profile.Parent, "--template", "2d",
            "--editor-version", "6000.6.7f1", "--no-cloud", "--with-pipeline"
        }), "project creation uses only approved folder/name/2D/version and no cloud");
    var status = UnityCliPolicy.Build("unity_status", profile);
    Check(!status.ChangesFiles &&
        status.Arguments.SequenceEqual(new[] {
            "--no-banner", "--format", "json", "status", "--project-path", profile.Project
        }), "Unity status is read-only and no arbitrary args");
    Deny(() => UnityCliPolicy.Build("powershell", profile),
        "arbitrary command blocked");
    Deny(() => UnityCliPolicy.Build("unity_project_open", profile),
        "cannot open missing project");
    string project = Path.Combine(parent, UnityCliPolicy.Name);
    Directory.CreateDirectory(project);
    Deny(() => UnityCliPolicy.Build("unity_project_create", profile),
        "cannot overwrite existing folder");
    Deny(() => UnityCliPolicy.Build("unity_project_open", profile),
        "cannot open folder without real Unity project");
    Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
    Directory.CreateDirectory(Path.Combine(project, "Assets"));
    File.WriteAllText(Path.Combine(project, "ProjectSettings", "ProjectVersion.txt"),
        "m_EditorVersion: 6000.6.7f1");
    var open = UnityCliPolicy.Build("unity_project_open", profile);
    Check(open.Arguments.Length == 5 &&
        open.Arguments[3] == "open" &&
        open.Arguments[4] == project,
        "open is restricted to locally approved project");
    Deny(() => UnityCliPolicy.Build("unity_project_create --path ../", profile),
        "no shell or arbitrary project path");
    Deny(() => UnityCliPolicy.ValidateParent(root + "-missing"),
        "missing parent blocked");
    Deny(() => UnityCliPolicy.ValidateParent(Path.GetPathRoot(root)!),
        "drive root blocked");
    Check(UnityCliBridge.HasVerifiedReadyJson(
        """{"editors":[{"state":"ready","project":"C:\\test\\uUnityDemo"}]}"""),
        "JSON exact ready plus expected demo project accepted");
    Check(!UnityCliBridge.HasVerifiedReadyJson(
        """{"state":"not_ready","project":"C:\\test\\uUnityDemo"}"""),
        "nonready state cannot falsely pass");
    Check(!UnityCliBridge.HasVerifiedReadyJson(
        """{"ready":false,"project":"C:\\test\\uUnityDemo"}"""),
        "false readiness cannot falsely pass");
    Check(!UnityCliBridge.HasVerifiedReadyJson(
        """{"state":"ready","project":"C:\\other\\Project"}"""),
        "another project cannot pass the demo status");
    Check(!UnityCliBridge.HasVerifiedReadyJson("not json"),
        "unstructured CLI status fails closed");
    string sensitive = "Error: Bearer SUPERPRIVATE_ACCESS_TOKEN abc\n" +
        "Exception in C:\\Users\\secretuser\\Desktop\\game\\sample.cs\n" +
        "warning https://example.invalid/log?auth=123\n" +
        "Failed to build for person@example.com";
    string[] safeLines = UnityDiagnosticLog.Excerpt(sensitive, errorsOnly: false);
    string joined = string.Join("\n", safeLines);
    Check(safeLines.Length == 4, "CLI excerpts remain line-bounded");
    Check(!joined.Contains("SUPERPRIVATE_ACCESS_TOKEN") &&
        !joined.Contains("secretuser") &&
        !joined.Contains("example.invalid") &&
        !joined.Contains("person@example.com"),
        "No tokens, local user paths, URLs or emails escape CLI report");
    Check(joined.Contains("[secret]") && joined.Contains("[local path]") &&
        joined.Contains("[url]") && joined.Contains("[email]"),
        "Sensitive data replaced by safe markers");
    string[] tail = UnityDiagnosticLog.Excerpt(
        string.Join("\n", Enumerable.Range(0, 22).Select(i => "error message " + i)),
        errorsOnly: true);
    Check(tail.Length == 10 && tail[0] == "error message 12" &&
        tail[^1] == "error message 21",
        "Only the final ten console errors remain");
    Check(UnityDiagnosticLog.Excerpt("Unity started\nno diagnostics", true).Length == 0,
        "No invented error messages when Editor.log has no errors");
    Check(UnityDiagnosticLog.Sanitize(new string('x', 800)).Length <= 181,
        "Single-line diagnostic length limited");
    string normalizedDemo = profile.Project.Replace('\\', '/');
    Check(UnityDiagnosticLog.HeaderMatchesApprovedProject(
        "Unity Editor start -projectPath " + normalizedDemo + "\n", profile.Project),
        "Editor.log verified for exact approved project");
    Check(!UnityDiagnosticLog.HeaderMatchesApprovedProject(
        "Unity Editor start -projectPath " + normalizedDemo + "Backup\n", profile.Project),
        "Different project with same prefix cannot expose log");
    Check(!UnityDiagnosticLog.HeaderMatchesApprovedProject(
        "Unity Editor start -projectPath C:/other/Project\n", profile.Project),
        "Unrelated Unity Editor logs rejected");
    string metaWarning = "Asset Packages/com.sunpole.uunitybridge/Editor has no meta file, but it's in an immutable folder. The asset will be ignored.";
    Check(UnityDiagnosticLog.Excerpt(metaWarning, true).Length == 1,
        "Unity immutable package no-meta warning is visible in Editor console");
    var editorProcess = new UnityDiagnosticLog.UnityProcess(
        "uUnityDemo - SampleScene - Windows, Mac, Linux - Unity 6.6 (6000.6.7f1) <DX11>",
        @"C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe",
        DateTimeOffset.UtcNow.AddMinutes(-5));
    // The exact project may not be in Editor.log's startup header, so
    // corroborate from the exclusive running Editor and a fresh log file.
    string verified = UnityDiagnosticLog.ValidateLogSource(
        "Unity Editor startup (no projectPath line)", profile.Project,
        new[] { editorProcess }, DateTimeOffset.UtcNow);
    Check(verified == "VERIFIED_EXCLUSIVE_RUNNING_EDITOR",
        "Editor.log accepted for exclusive approved Unity project and fresh session");
    Check(UnityDiagnosticLog.ValidateLogSource(
        "No project path", profile.Project,
        new[] { editorProcess, editorProcess }, DateTimeOffset.UtcNow) ==
        "BLOCKED_EDITOR_IDENTITY", "multiple Editor processes cannot expose log");
    var unrelatedEditor = editorProcess with {
        Title = "AnotherProject - SampleScene - Unity 6.6"
    };
    Check(UnityDiagnosticLog.ValidateLogSource(
        "No path", profile.Project, new[] { unrelatedEditor }, DateTimeOffset.UtcNow) ==
        "BLOCKED_EDITOR_IDENTITY", "different project's console is never read");
    Check(UnityDiagnosticLog.ValidateLogSource(
        "No path", profile.Project, new[] { editorProcess },
        DateTimeOffset.UtcNow.AddHours(-2)) == "BLOCKED_EDITOR_LOG_STALE",
        "stale Unity Editor log cannot be reported as current");
    Console.WriteLine($"PASS: {assertions} isolated Unity CLI policy checks. No Editor launched.");
}
finally
{
    Directory.Delete(root, recursive: true);
}
