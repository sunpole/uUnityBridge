using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace uUnityBridge.Agent;

/// <summary>
/// GitHub Issues is an OUTBOUND-only relay. It never supplies executable code,
/// paths, process arguments or shell commands. Deploy jobs use a strictly
/// typed release reference and a separately approved machine-local profile.
/// </summary>
internal sealed record RelayTask(
    int IssueNumber, string Id, string Action = "status",
    string? Profile = null, string? ReleaseTag = null,
    string? Sha256 = null, long ReleaseId = 0, long AssetId = 0, long RunId = 0);

internal interface IRelayIssueClient
{
    Task<string> ReadOpenIssuesAsync(CancellationToken ct);
    Task<bool> HasResponseAsync(RelayTask task, CancellationToken ct);
    Task PublishResponseAsync(RelayTask task, string report, CancellationToken ct);
    Task CloseCompletedAsync(RelayTask task, CancellationToken ct);
}

internal static class RelayProtocol
{
    internal const string Repo = "sunpole/uUnityBridge";
    private static readonly Regex IdPattern = new(
        @"^R[0-9]{6}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex TagPattern = new(
        @"^v[0-9]{1,6}\.[0-9]{1,6}\.[0-9]{1,6}$", RegexOptions.CultureInvariant);
    private static readonly Regex ProfilePattern = new(
        @"^[a-z][a-z0-9-]{1,31}$", RegexOptions.CultureInvariant);
    private static readonly Regex ShaPattern = new(
        @"^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    internal static string Title(string id) => $"[UUB JOB {id}]";

    internal static IReadOnlyList<RelayTask> Parse(string listJson)
    {
        if (listJson.Length > 250_000) throw new InvalidDataException("Issue list too large.");
        using var doc = JsonDocument.Parse(listJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Issue list is not an array.");

        var output = new List<RelayTask>();
        var numbers = new HashSet<int>();
        foreach (var issue in doc.RootElement.EnumerateArray())
        {
            try
            {
                if (issue.ValueKind != JsonValueKind.Object) continue;
                int number = issue.GetProperty("number").GetInt32();
                if (number < 1 || !numbers.Add(number)) continue;
                string? author = issue.GetProperty("author")
                    .GetProperty("login").GetString();
                if (!string.Equals(author, "sunpole", StringComparison.Ordinal))
                    continue;
                string? title = issue.GetProperty("title").GetString();
                string? body = issue.GetProperty("body").GetString();
                if (body is null || body.Length is < 1 or > 2048) continue;

                using var json = JsonDocument.Parse(body);
                var request = json.RootElement;
                if (request.ValueKind != JsonValueKind.Object ||
                    request.GetProperty("schema").GetString() != "uub.agent.job.v1")
                    continue;

                string id = request.GetProperty("id").GetString() ?? "";
                string action = request.GetProperty("action").GetString() ?? "";
                if (!IdPattern.IsMatch(id) || title != Title(id)) continue;

                if (action == "status" && request.EnumerateObject().Count() == 3)
                    output.Add(new RelayTask(number, id));
                else if (action == "install_release" && request.EnumerateObject().Count() == 9)
                {
                    string profile = request.GetProperty("profile").GetString() ?? "";
                    string tag = request.GetProperty("tag").GetString() ?? "";
                    string sha = request.GetProperty("sha256").GetString() ?? "";
                    long releaseId = request.GetProperty("releaseId").GetInt64();
                    long assetId = request.GetProperty("assetId").GetInt64();
                    long runId = request.GetProperty("runId").GetInt64();
                    if (!ProfilePattern.IsMatch(profile) || !TagPattern.IsMatch(tag) ||
                        !ShaPattern.IsMatch(sha) || releaseId < 1 || assetId < 1 || runId < 1)
                        continue;
                    output.Add(new RelayTask(number, id, action, profile, tag,
                        sha, releaseId, assetId, runId));
                }
                if (output.Count == 5) break; // bounded work per poll
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                // Untrusted Issues are never treated as code or instructions.
            }
        }
        return output;
    }

    internal static string Marker(string id) => $"<!-- uub-agent-ans:v1:{id} -->";

    internal static string DeploymentResponse(RelayTask job, DeploymentResult result) =>
        $"## uUnityBridge Agent · {job.Id}\n\n" +
        $"Action: install_release\n" +
        $"Status: **{result.Status}**\n" +
        $"Result: {result.Code}\n" +
        "Scope: local pre-approved profile; no arbitrary commands\n" +
        "Validation: release/asset identity, CI commit, SHA256, ZIP paths\n" +
        "Runtime launch/GUI acceptance: not tested\n\n" + Marker(job.Id);

    internal static string Response(RelayTask task, bool accountOk, string? branch)
    {
        string status = accountOk && branch is not null ? "PASS" : "BLOCKED";
        // No personal data, machine names, usernames, paths or API credentials.
        return $"## uUnityBridge Agent · {task.Id}\n\n" +
            $"Status: **{status}**\n" +
            "Agent: current-user IPC, non-admin\n" +
            $"GitHub account: {(accountOk ? "verified" : "unavailable")}\n" +
            $"Repository: {Repo}\n" +
            $"Default branch: {(branch ?? "unavailable")}\n\n" +
            Marker(task.Id);
    }
}

internal static class GitHubRelay
{
    internal static async Task<int> RunOnceAsync(
        IRelayIssueClient issues, IGitHubReader reader, CancellationToken ct,
        IDeploymentExecutor? deployer = null)
    {
        if (!await reader.IsExpectedOwnerAsync(ct)) return 0;
        var tasks = RelayProtocol.Parse(await issues.ReadOpenIssuesAsync(ct));
        int completed = 0;
        foreach (var job in tasks)
        {
            ct.ThrowIfCancellationRequested();
            if (!await issues.HasResponseAsync(job, ct))
            {
                string report;
                if (job.Action == "install_release")
                {
                    deployer ??= new ReleaseDeployment();
                    DeploymentResult result = await deployer.ExecuteAsync(job, ct);
                    report = RelayProtocol.DeploymentResponse(job, result);
                }
                else
                {
                    string? branch = await reader.GetDefaultBranchAsync(ct);
                    report = RelayProtocol.Response(job, true, branch);
                }
                await issues.PublishResponseAsync(job, report, ct);
            }
            await issues.CloseCompletedAsync(job, ct);
            completed++;
        }
        return completed;
    }

    internal static async Task PollAsync(IGitHubReader reader, CancellationToken ct)
    {
        IRelayIssueClient issues = new CliRelayIssueClient();
        using var interval = new PeriodicTimer(TimeSpan.FromSeconds(60));
        // The local health check is available immediately; no call to GitHub
        // should hold the named-pipe server open.
        try
        {
            while (await interval.WaitForNextTickAsync(ct))
            {
                try
                {
                    using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    limit.CancelAfter(TimeSpan.FromMinutes(8));
                    _ = await RunOnceAsync(issues, reader, limit.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    Console.Error.WriteLine("Relay poll timed out.");
                }
                catch (Exception e) when (e is IOException or InvalidOperationException
                    or JsonException or InvalidDataException or FormatException)
                {
                    Console.Error.WriteLine("Relay unavailable: " + e.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}

internal sealed class CliRelayIssueClient : IRelayIssueClient
{
    public Task<string> ReadOpenIssuesAsync(CancellationToken ct) => RunAsync(
        ["issue", "list", "-R", RelayProtocol.Repo, "--state", "open",
            "--limit", "50", "--json", "number,title,body,author"], ct);

    public async Task<bool> HasResponseAsync(RelayTask task, CancellationToken ct)
    {
        string content = await RunAsync(["issue", "view", task.IssueNumber.ToString(),
            "-R", RelayProtocol.Repo, "--json", "title,author,comments"], ct);
        using var doc = JsonDocument.Parse(content);
        var issue = doc.RootElement;
        if (issue.GetProperty("title").GetString() != RelayProtocol.Title(task.Id) ||
            issue.GetProperty("author").GetProperty("login").GetString() != "sunpole")
            throw new InvalidDataException("Issue author/title changed.");

        foreach (var comment in issue.GetProperty("comments").EnumerateArray())
        {
            if (comment.GetProperty("author").GetProperty("login").GetString() != "sunpole")
                continue;
            var body = comment.GetProperty("body").GetString() ?? "";
            if (body.Contains(RelayProtocol.Marker(task.Id), StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public async Task PublishResponseAsync(RelayTask task, string report, CancellationToken ct)
    {
        // Fixed repo, fixed gh verbs, only our own bounded formatted status string.
        _ = await RunAsync(["issue", "comment", task.IssueNumber.ToString(), "-R",
            RelayProtocol.Repo, "--body", report], ct);
    }

    public async Task CloseCompletedAsync(RelayTask task, CancellationToken ct)
    {
        // Recheck the expected author and response marker before changing state.
        if (!await HasResponseAsync(task, ct))
            throw new InvalidDataException("No verified response comment.");
        _ = await RunAsync(["issue", "close", task.IssueNumber.ToString(), "-R",
            RelayProtocol.Repo, "--reason", "completed"], ct);
    }

    // Inputs are fixed and validated. NEVER accept a command from GitHub Issue body.
    private static async Task<string> RunAsync(string[] arguments, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("gh")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string arg in arguments) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        string text = await stdout;
        _ = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException("gh operation failed.");
        if (text.Length > 250_000) throw new InvalidDataException("GitHub response too large.");
        return text.Trim();
    }
}

internal static class RelayTests
{
    private sealed class FakeReader : IGitHubReader
    {
        internal bool Auth = true;
        internal string? Branch = "main";
        public Task<bool> IsExpectedOwnerAsync(CancellationToken _) => Task.FromResult(Auth);
        public Task<string?> GetDefaultBranchAsync(CancellationToken _) =>
            Task.FromResult(Branch);
    }

    private sealed class FakeIssues : IRelayIssueClient
    {
        internal string Json = "[]";
        internal readonly List<string> Reports = [];
        internal readonly HashSet<int> Marked = [];
        internal readonly List<int> Closed = [];
        public Task<string> ReadOpenIssuesAsync(CancellationToken _) => Task.FromResult(Json);
        public Task<bool> HasResponseAsync(RelayTask job, CancellationToken _) =>
            Task.FromResult(Marked.Contains(job.IssueNumber));
        public Task PublishResponseAsync(RelayTask job, string report, CancellationToken _)
        {
            Reports.Add(report);
            Marked.Add(job.IssueNumber);
            return Task.CompletedTask;
        }
        public Task CloseCompletedAsync(RelayTask job, CancellationToken _)
        {
            if (!Marked.Contains(job.IssueNumber)) throw new Exception("Unsafe issue close.");
            Closed.Add(job.IssueNumber);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDeployer : IDeploymentExecutor
    {
        internal int Calls;
        public Task<DeploymentResult> ExecuteAsync(RelayTask _, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new DeploymentResult("BLOCKED", "local_approval_missing"));
        }
    }

    private static string Issue(int number, string id, string author, string action,
        bool extraField = false)
    {
        var body = new Dictionary<string, string>
        {
            ["schema"] = "uub.agent.job.v1", ["id"] = id, ["action"] = action
        };
        if (extraField) body["command"] = "powershell";
        return JsonSerializer.Serialize(new
        {
            number, title = RelayProtocol.Title(id), author = new { login = author },
            body = JsonSerializer.Serialize(body)
        });
    }

    internal static async Task RunAsync()
    {
        const string id = "R000001";
        string valid = Issue(71, id, "sunpole", "status");
        string foreign = Issue(72, "R000002", "someone_else", "status");
        string dangerous = Issue(73, "R000003", "sunpole", "powershell");
        string extra = Issue(74, "R000004", "sunpole", "status", true);
        var items = RelayProtocol.Parse("[" + string.Join(",",valid,foreign,dangerous,extra) + "]");
        if (items.Count != 1 || items[0] != new RelayTask(71, id))
            throw new Exception("Issue schema/author/command rejection failed.");
        var issues = new FakeIssues { Json = "[" + valid + "]" };
        var reader = new FakeReader();
        if (await GitHubRelay.RunOnceAsync(issues, reader, CancellationToken.None) != 1 ||
            issues.Reports.Count != 1 || issues.Closed.Count != 1 ||
            !issues.Reports[0].Contains(RelayProtocol.Marker(id), StringComparison.Ordinal))
            throw new Exception("Relay status reporting failed.");
        // Re-run must not post the same response twice.
        _ = await GitHubRelay.RunOnceAsync(issues, reader, CancellationToken.None);
        if (issues.Reports.Count != 1) throw new Exception("Duplicate reply on retry.");
        reader.Auth = false;
        _ = await GitHubRelay.RunOnceAsync(issues, reader, CancellationToken.None);
        if (issues.Reports.Count != 1) throw new Exception("Untrusted account wrote a reply.");
        string deployBody = JsonSerializer.Serialize(new
        {
            schema = "uub.agent.job.v1", id = "R000005", action = "install_release",
            profile = "unity-test", tag = "v1.2.3",
            sha256 = new string('a', 64), releaseId = 11, assetId = 22, runId = 33
        });
        string deployJson = JsonSerializer.Serialize(new
        {
            number = 75, title = RelayProtocol.Title("R000005"),
            author = new { login = "sunpole" }, body = deployBody
        });
        var parsed = RelayProtocol.Parse("[" + deployJson + "]");
        if (parsed.Count != 1 || parsed[0].Action != "install_release" ||
            parsed[0].ReleaseId != 11 || parsed[0].AssetId != 22)
            throw new Exception("Typed deployment job rejected.");
        var fakeDeploy = new FakeDeployer();
        var deployIssues = new FakeIssues { Json = "[" + deployJson + "]" };
        reader.Auth = true;
        _ = await GitHubRelay.RunOnceAsync(deployIssues, reader, CancellationToken.None, fakeDeploy);
        _ = await GitHubRelay.RunOnceAsync(deployIssues, reader, CancellationToken.None, fakeDeploy);
        if (fakeDeploy.Calls != 1 || deployIssues.Reports.Count != 1 ||
            !deployIssues.Reports[0].Contains("local_approval_missing"))
            throw new Exception("Deployment must be idempotent and fail closed.");
        RelayTask bad = new(76, "R000006");
        if (RelayProtocol.Parse("[" + JsonSerializer.Serialize(new
        {
            number = 76, title = RelayProtocol.Title(bad.Id),
            author = new { login = "someone_else" }, body = deployBody
        }) + "]").Count != 0)
            throw new Exception("Foreign deployment author accepted.");
        foreach (string unsafePath in new[] { "../x.exe", "C:/secret", "folder/../x",
            "x\\evil.exe", "CON.txt", "folder/.uub-managed.json", "/root/file.exe" })
        {
            try { ReleaseDeployment.ValidateArchivePath(unsafePath); }
            catch (InvalidDataException) { continue; }
            throw new Exception("Unsafe archive path accepted.");
        }
        Console.WriteLine("PASS: relay schema, trusted author, denied commands, deployment gate, idempotence, archive path checks.");
    }
}
