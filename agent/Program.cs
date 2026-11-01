using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Security.Principal;

namespace uUnityBridge.Agent;

// P0008 stage 1: an IPC API, NOT a network listener or an administrator shell.
// PipeOptions.CurrentUserOnly is the OS-level caller boundary.
internal static class Program
{
    private const string PipeName = "uUnityBridge.agent.v1";
    private const int MaxRequestCharacters = 4096;

    private static async Task<int> Main(string[] args)
    {
        if (args is ["--self-test"])
            return await SelfTest.RunAsync();

        if (args is ["--diagnose"])
        {
            // A local, read-only check requiring no pipe connection or GitHub token.
            using var identity = WindowsIdentity.GetCurrent();
            bool elevated = new WindowsPrincipal(identity)
                .IsInRole(WindowsBuiltInRole.Administrator);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                service = "uUnityBridge.Agent", version = "0.3.0-preview",
                isElevated = elevated,
                expectedExecution = "same standard Windows user/session for server and client"
            }));
            return 0;
        }

        if (args is ["--relay-once"])
        {
            try
            {
                int done = await GitHubRelay.RunOnceAsync(
                    new CliRelayIssueClient(), new GitHubReader(), CancellationToken.None);
                Console.WriteLine($"Relay processed {done} tasks.");
                return 0;
            }
            catch (Exception e) when (e is IOException or InvalidOperationException
                or InvalidDataException or JsonException)
            {
                Console.Error.WriteLine("Relay cannot complete: " + e.GetType().Name);
                return 2;
            }
        }

        if (args is ["--request", var action])
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName,
                    PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                await pipe.ConnectAsync(3000, timeout.Token);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
                { AutoFlush = true };
                await writer.WriteLineAsync(JsonSerializer.Serialize(new { action }));
                string? reply = await ReadBoundedLineAsync(reader, timeout.Token);
                if (reply is null) throw new IOException("Local agent did not respond.");
                Console.WriteLine(reply);
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                // PipeOptions.CurrentUserOnly on Windows is sensitive to the
                // process security context and elevation. Do not silently
                // weaken the pipe security or request administrator rights.
                Console.Error.WriteLine("BLOCKED: Named Pipe access denied. " +
                    "Run both agent and client as the same standard Windows user. " +
                    "Do not elevate either process.");
                return 4;
            }
            catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException)
            {
                Console.Error.WriteLine("Agent connection failed: " + e.GetType().Name);
                return 2;
            }
        }

        if (args is not [] and not ["--serve"])
        {
            Console.Error.WriteLine("Use --serve, --relay-once, --request <action>, --diagnose, or --self-test.");
            return 2;
        }

        // A background agent must never inherit admin privileges accidentally.
        // Local project and GitHub read/write functionality should not run
        // elevated; future privileged operations will require a separate broker.
        using (var identity = WindowsIdentity.GetCurrent())
        {
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            {
                Console.Error.WriteLine("BLOCKED: Start the agent in a non-elevated Windows session.");
                return 5;
            }
        }

        using var mutex = new Mutex(true, @"Local\uUnityBridge.Agent.v1", out bool ownsMutex);
        if (!ownsMutex)
        {
            Console.Error.WriteLine("Agent already running in this Windows session.");
            return 3;
        }

        Console.WriteLine("uUnityBridge Agent 0.2.1 preview: current-user IPC + outbound GitHub relay; no admin tools.");
        Console.WriteLine("Read-only actions: health, capabilities, github_status, repo_status, workflow_preview.");
        Console.WriteLine("No TCP ports, no PowerShell. Only typed authorized GitHub jobs. Ctrl+C to stop.");
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        IGitHubReader github = new GitHubReader();
        Task relayLoop = GitHubRelay.PollAsync(github, stop.Token);

        while (!stop.IsCancellationRequested)
        {
            using var server = new NamedPipeServerStream(PipeName,
                PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await server.WaitForConnectionAsync(stop.Token); }
            catch (OperationCanceledException) { break; }

            // One bounded request per connection. No remote command strings.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true)
                { AutoFlush = true };
                string? input = await ReadBoundedLineAsync(reader, timeout.Token);
                object reply = input is null
                    ? new { status = "error", error = "empty_request" }
                    : await AgentProtocol.RunAsync(input, github, timeout.Token);
                await writer.WriteLineAsync(JsonSerializer.Serialize(reply));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or OperationCanceledException)
            {
                // Deliberately do not print request bodies, paths, tokens or error internals.
                Console.Error.WriteLine("Rejected request: " + e.GetType().Name);
            }
        }
        await relayLoop;
        return 0;
    }

    private static async Task<string?> ReadBoundedLineAsync(StreamReader reader, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buffer = new char[1];
        while (sb.Length <= MaxRequestCharacters)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(0, 1), ct);
            if (count == 0) return sb.Length == 0 ? null : sb.ToString();
            if (buffer[0] == '\n') return sb.ToString().TrimEnd('\r');
            sb.Append(buffer[0]);
        }
        throw new InvalidDataException("Request exceeds allowed size.");
    }
}

internal interface IGitHubReader
{
    Task<bool> IsExpectedOwnerAsync(CancellationToken ct);
    Task<string?> GetDefaultBranchAsync(CancellationToken ct);
}

internal sealed class GitHubReader : IGitHubReader
{
    private const string Repo = "sunpole/uUnityBridge";

    public async Task<bool> IsExpectedOwnerAsync(CancellationToken ct)
    {
        try
        {
            var login = await RunGhAsync(["api", "user", "--jq", ".login"], ct);
            return string.Equals(login.Trim(), "sunpole", StringComparison.Ordinal);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or OperationCanceledException)
        {
            return false;
        }
    }

    public async Task<string?> GetDefaultBranchAsync(CancellationToken ct)
    {
        if (!await IsExpectedOwnerAsync(ct)) return null;
        try
        {
            var json = await RunGhAsync(["repo", "view", Repo,
                "--json", "defaultBranchRef"], ct);
            using var doc = JsonDocument.Parse(json);
            var branch = doc.RootElement.GetProperty("defaultBranchRef")
                .GetProperty("name").GetString();
            return branch is "main" or "master" ? branch : null;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException
            or JsonException or KeyNotFoundException or OperationCanceledException)
        {
            return null;
        }
    }

    // No caller-controlled arguments, no PowerShell, and no GitHub write verbs.
    private static async Task<string> RunGhAsync(string[] args, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("gh")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        string output = await stdoutTask;
        _ = await stderrTask; // Never log error output: it could contain sensitive details.
        if (process.ExitCode != 0) throw new InvalidOperationException("GitHub CLI unavailable.");
        if (output.Length > 8192) throw new InvalidDataException("GitHub result is too large.");
        return output;
    }
}

internal static class AgentProtocol
{
    internal static async Task<object> RunAsync(string request, IGitHubReader github, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(request);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Count() != 1 ||
                !root.TryGetProperty("action", out var action) ||
                action.ValueKind != JsonValueKind.String)
                return new { status = "error", error = "invalid_request" };

            return action.GetString() switch
            {
                "health" => new
                {
                    status = "ok", service = "uUnityBridge.Agent", version = "0.3.0-preview",
                    mode = "read-only", adminToolsEnabled = false, transport = "current-user-named-pipe"
                },
                "capabilities" => new
                {
                    status = "ok",
                    actions = new[] { "health", "capabilities", "github_status", "repo_status", "workflow_preview" },
                    commandExecution = false, remoteNetworkListener = false
                },
                "github_status" => new
                {
                    status = "ok",
                    expectedAccountAuthenticated = await github.IsExpectedOwnerAsync(ct)
                },
                "repo_status" => new
                {
                    status = "ok", repository = "sunpole/uUnityBridge",
                    defaultBranch = await github.GetDefaultBranchAsync(ct)
                },
                "workflow_preview" => await PreviewAsync(github, ct),
                _ => new { status = "error", error = "unknown_action" }
            };
        }
        catch (JsonException) { return new { status = "error", error = "invalid_json" }; }
    }

    private static async Task<object> PreviewAsync(IGitHubReader github, CancellationToken ct)
    {
        bool authenticated = await github.IsExpectedOwnerAsync(ct);
        var checks = new List<object>
        {
            new { step = 1, name = "agent_health", state = "PASS" },
            new { step = 2, name = "gh_owner", state = authenticated ? "PASS" : "BLOCKED" }
        };
        if (authenticated)
        {
            string? branch = await github.GetDefaultBranchAsync(ct);
            checks.Add(new
            {
                step = 3, name = "repository_branch",
                state = branch is not null ? "PASS" : "BLOCKED"
            });
        }
        return new
        {
            status = authenticated && checks.Count == 3 &&
                await github.GetDefaultBranchAsync(ct) is not null ? "PASS" : "BLOCKED",
            plan = "read-only diagnostics", checks
        };
    }
}

internal static class SelfTest
{
    private sealed class FakeGitHub : IGitHubReader
    {
        public bool Owner { get; set; } = true;
        public string? Branch { get; set; } = "main";
        public Task<bool> IsExpectedOwnerAsync(CancellationToken ct) => Task.FromResult(Owner);
        public Task<string?> GetDefaultBranchAsync(CancellationToken ct) =>
            Task.FromResult(Branch);
    }

    internal static async Task<int> RunAsync()
    {
        var fake = new FakeGitHub();
        var cases = new[]
        {
            (input: "{\"action\":\"health\"}", expected: "ok"),
            (input: "{\"action\":\"capabilities\"}", expected: "ok"),
            (input: "{\"action\":\"github_status\"}", expected: "ok"),
            (input: "{\"action\":\"repo_status\"}", expected: "ok"),
            (input: "{\"action\":\"workflow_preview\"}", expected: "PASS"),
            (input: "{\"action\":\"powershell\"}", expected: "error"),
            (input: "{\"action\":\"delete_files\"}", expected: "error"),
            (input: "{\"action\":\"health\",\"command\":\"Get-Process\"}", expected: "error"),
            (input: "not json", expected: "error")
        };
        foreach (var test in cases)
        {
            object response = await AgentProtocol.RunAsync(test.input, fake, CancellationToken.None);
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(response));
            string? actual = doc.RootElement.GetProperty("status").GetString();
            if (actual != test.expected)
                throw new Exception($"Self test failed. Input: {test.input}");
        }

        fake.Owner = false;
        object blocked = await AgentProtocol.RunAsync("{\"action\":\"workflow_preview\"}",
            fake, CancellationToken.None);
        using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(blocked)))
        {
            if (doc.RootElement.GetProperty("status").GetString() != "BLOCKED")
                throw new Exception("Untrusted GH identity was not blocked.");
        }
        await RelayTests.RunAsync();
        Console.WriteLine($"PASS: {cases.Length + 1} agent protocol tests and GitHub relay tests; no user files touched.");
        return 0;
    }
}
