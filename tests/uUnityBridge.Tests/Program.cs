using System.Text.Json;
using uUnityBridge;

int checks = 0;
void Check(bool condition, string name)
{
    checks++;
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
}

void MustReject(Action work, string name)
{
    bool failed = false;
    try { work(); } catch (Exception ex) when (ex is InvalidDataException or JsonException) { failed = true; }
    Check(failed, name);
}

var patch = new Patch(2, "P0002", "[PATCH P0002]", "Update safe queue", "sync-main",
    "0123456789abcdef0123456789abcdef01234567", "https://github.com/sunpole/uUnityBridge/issues/2");
string fp = PatchWorkflow.Fingerprint(patch);
Check(fp.Length == 64, "patch SHA-256 fingerprint");
Check(fp == PatchWorkflow.Fingerprint(patch), "fingerprint deterministic");
Check(fp != PatchWorkflow.Fingerprint(patch with { Description = "Edited Issue" }), "edited body detected");
var receipt = new PatchReceipt(2, "P0002", "FAIL", "Temporary error", patch.Url,
    false, null, fp, 1, DateTimeOffset.UtcNow);
Check(PatchWorkflow.Matches(patch, receipt), "receipt bound to exact patch");
Check(!PatchWorkflow.Matches(patch with { Commit = new string('a', 40) }, receipt), "different commit rejected");
Check(PatchWorkflow.ShouldAttempt(null), "new patch can run");
Check(PatchWorkflow.ShouldAttempt(receipt), "failed patch retries under cap");
Check(!PatchWorkflow.ShouldAttempt(receipt with { Attempts = 3 }), "retry limit enforced");
Check(!PatchWorkflow.ShouldAttempt(receipt with { State = "APPLYING" }), "interrupted operation does not replay");
Check(!PatchWorkflow.ShouldAttempt(receipt with { State = "BLOCKED" }), "blocked operation does not replay");
Check(!PatchWorkflow.IsSendable(receipt), "temporary failure does not publish yet");
Check(PatchWorkflow.IsSendable(receipt with { State = "BLOCKED" }), "terminal failure publishes");
Check(PatchWorkflow.IsSendable(receipt with { State = "SOURCE_UPDATED" }), "source update can publish");
Check(!PatchWorkflow.IsSuccess(receipt with { State = "BLOCKED" }), "blocked state cannot close issue");
Check(PatchWorkflow.IsSuccess(receipt with { State = "PASS" }), "completed check can close issue");
Check(PatchWorkflow.AnswerMarker(receipt).Contains("P0002:" + fp + ":1"), "ANS includes attempt identity");
Check(PatchWorkflow.AnswerMarker(receipt) != PatchWorkflow.AnswerMarker(receipt with { Attempts = 2 }),
    "new attempt never reuses obsolete ANS");

string json = JsonSerializer.Serialize(new[] { receipt });
Check(PatchHistory.Parse(json)[2].Fingerprint == fp, "ledger roundtrip");
string legacy = """[{"Number":1,"Id":"P0001","State":"PASS","Summary":"OK","IssueUrl":"https://example.org","Posted":true,"AnswerUrl":null}]""";
var old = PatchHistory.Parse(legacy)[1];
Check(old.Attempts == 1, "v0.2 history reads with default attempt");
Check(PatchWorkflow.Matches(new Patch(1,"P0001","[PATCH P0001]","","report-only",null,""),old),
    "legacy no-op receipt accepted");
Check(!PatchWorkflow.Matches(patch,old), "legacy receipt cannot authorize code update");
MustReject(() => PatchHistory.Parse(json.Replace("\"P0002\"", "\"NOT_A_PATCH\"")), "invalid patch ID rejected");
MustReject(() => PatchHistory.Parse("[" + json.TrimStart('[').TrimEnd(']') + "," +
    json.TrimStart('[').TrimEnd(']') + "]"), "duplicate issue number rejected");
MustReject(() => PatchHistory.Parse("malformed-json"), "malformed history rejected");

var previous = receipt with { State = "PASS", Posted = true, AnswerUrl = "https://github.com/sunpole/uUnityBridge/issues/2#issuecomment-1" };
var copied = new HashSet<string>(StringComparer.Ordinal) { ReportInbox.Key(previous) };
Check(ReportInbox.Pending(new[] { previous }, copied).Count == 0, "previously copied ANS hidden");
Check(ReportInbox.Pending(new[] { previous with { Closed = true, UpdatedAt = DateTimeOffset.UtcNow } }, copied).Count == 0,
    "closing Issue does not create a duplicate notification");
var second = previous with { Number = 3, Id = "P0003", AnswerUrl = "https://github.com/sunpole/uUnityBridge/issues/3#issuecomment-1" };
Check(ReportInbox.Pending(new[] { previous, second }, copied).Count == 1, "only new ANS becomes reportable");
Check(ReportInbox.Pending(new[] { second with { Posted = false } }, copied).Count == 0, "unpublished result never reported");
var roundtrip = ReportInbox.Parse(System.Text.Json.JsonSerializer.Serialize(copied.Order(StringComparer.Ordinal).ToArray()));
Check(roundtrip.SetEquals(copied), "cursor serializes without losing acknowledged ANS");
MustReject(() => ReportInbox.Parse("not json"), "corrupted cursor rejected");

Check(ReleaseUpdater.TryVersion("v0.4.1", out var releaseV) && releaseV == new Version(0, 4, 1),
    "stable release tag parses correctly");
Check(!ReleaseUpdater.TryVersion("v0.4.1-rc.1", out _), "prerelease tag rejected");
Check(!ReleaseUpdater.TryVersion("../../v0.4.1", out _), "invalid path-like tag rejected");
Check(!ReleaseUpdater.TryVersion("v0.4", out _), "incomplete tag rejected");

// v0.5 pure manifest and delta-planner regression tests: no local files touched.
string aHash = new string('a', 64);
string bHash = new string('b', 64);
var currentPackage = new PackageManifest(PackageFiles.Schema, "0.5.0", "portable", "win-x64",
    [new PackageEntry("uUnityBridge.exe", 151552, aHash),
     new PackageEntry("Core/uUnityBridge.dll", 8000, aHash),
     new PackageEntry("Modules/readme.txt", 100, aHash)]);
var futurePackage = currentPackage with
{
    Version = "0.5.1",
    Files = [new PackageEntry("uUnityBridge.exe", 151552, aHash),
             new PackageEntry("Core/uUnityBridge.dll", 9000, bHash),
             new PackageEntry("Modules/readme.txt", 100, aHash),
             new PackageEntry("Modules/new-feature.dll", 4000, bHash)]
};
PackageFiles.Validate(currentPackage);
PackageFiles.Validate(futurePackage);
Check(PackageFiles.MissingOrChanged(currentPackage, futurePackage)
    .Select(x => x.Path).SequenceEqual(["Core/uUnityBridge.dll", "Modules/new-feature.dll"]),
    "only changed and new DLL files selected");
Check(PackageFiles.MissingOrChanged(futurePackage, futurePackage).Count == 0,
    "identical manifest requires zero downloads");
string manifestJson = JsonSerializer.Serialize(futurePackage);
Check(PackageFiles.Parse(manifestJson).Version == "0.5.1", "manifest JSON roundtrip");
MustReject(() => PackageFiles.Validate(futurePackage with
    { Files = [..futurePackage.Files, new PackageEntry("core/UUNITYBRIDGE.DLL", 4, aHash)] }),
    "case-insensitive filename collision rejected");
MustReject(() => PackageFiles.Validate(futurePackage with
    { Files = [..futurePackage.Files, new PackageEntry("../outside.dll", 4, aHash)] }),
    "directory traversal rejected");
MustReject(() => PackageFiles.Validate(futurePackage with
    { Files = [..futurePackage.Files, new PackageEntry("Modules/COM1.dll", 4, aHash)] }),
    "Windows device filenames rejected");
MustReject(() => PackageFiles.Validate(futurePackage with
    { Files = [..futurePackage.Files, new PackageEntry("Modules/a:stream", 4, aHash)] }),
    "NTFS alternate data stream rejected");
MustReject(() => PackageFiles.Validate(futurePackage with
    { Files = [..futurePackage.Files, new PackageEntry("Modules/evil%2Fname.dll", 4, aHash)] }),
    "encoded separators rejected");
MustReject(() => PackageFiles.Validate(futurePackage with
    { Files = [..futurePackage.Files, new PackageEntry("Modules/bad.dll", 4, "INVALID")] }),
    "malformed SHA-256 rejected");
MustReject(() => PackageFiles.Validate(futurePackage with
    { Files = [new PackageEntry("Core/other.dll", 1, aHash)] }),
    "manifest missing executable rejected");
MustReject(() => PackageFiles.MissingOrChanged(currentPackage, futurePackage with
    { Profile = "compact" }), "profile migration cannot use incremental delta");

Console.WriteLine($"PASS: {checks} tests. No network calls or local user files modified.");
