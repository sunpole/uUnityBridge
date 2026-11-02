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
    Console.WriteLine($"PASS: {assertions} isolated Unity CLI policy checks. No Editor launched.");
}
finally
{
    Directory.Delete(root, recursive: true);
}
