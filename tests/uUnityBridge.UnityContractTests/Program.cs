using Sunpole.uUnityBridge.Editor;

int count = 0;
void Check(bool value, string what)
{
    if (!value) throw new Exception("FAIL: " + what);
    count++;
    Console.WriteLine("PASS: " + what);
}
void Reject(Action action, string what)
{
    bool rejected = false;
    try { action(); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, what);
}
var allowed = new Dictionary<string, UnityBridgeAction>
{
    ["unity_status"] = UnityBridgeAction.UnityStatus,
    ["create_scene"] = UnityBridgeAction.CreateScene,
    ["create_platform"] = UnityBridgeAction.CreatePlatform,
    ["save_scene"] = UnityBridgeAction.SaveScene
};
foreach (var (name, expected) in allowed)
    Check(UnityBridgePolicy.Parse(name) == expected, "approved: " + name);
foreach (var action in new[] { "powershell", "cmd", "exec", "delete_scene",
    "open_file", "unity_status; rm -rf", "CREATE_SCENE", "", "../create_scene" })
    Reject(() => UnityBridgePolicy.Parse(action), "denied unknown: " + action);
UnityBridgePolicy.ValidatePlatform(-20f, 20f, .2f, 4f);
Check(true, "boundary platform accepted");
UnityBridgePolicy.ValidatePlatform(0f, 0f, 3f, .4f);
Check(true, "normal platform accepted");
foreach (float bad in new[] { -21f, 21f, float.NaN, float.PositiveInfinity,
    float.NegativeInfinity })
    Reject(() => UnityBridgePolicy.ValidatePlatform(bad, 0f, 1f, 1f),
        "reject invalid coordinate: " + bad);
foreach (float bad in new[] { -1f, 0f, .1f, 12.1f, float.NaN, float.PositiveInfinity })
    Reject(() => UnityBridgePolicy.ValidatePlatform(0f, 0f, bad, 1f),
        "reject invalid platform width: " + bad);
foreach (float bad in new[] { -1f, 0f, .1f, 4.1f, float.NaN })
    Reject(() => UnityBridgePolicy.ValidatePlatform(0f, 0f, 3f, bad),
        "reject invalid platform height: " + bad);
Console.WriteLine($"PASS: {count} Unity Editor command contract checks (no Unity Editor was invoked).");
