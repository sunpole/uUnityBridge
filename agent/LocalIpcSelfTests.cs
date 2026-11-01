using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace uUnityBridge.Agent;

// CI runs this test within a single Windows runner account. It proves the
// actual current-user Named Pipe transport and protocol behavior without
// starting the persistent service or opening TCP ports.
internal static class LocalIpcSelfTests
{
    private sealed class NoNetworkReader : IGitHubReader
    {
        public Task<bool> IsExpectedOwnerAsync(CancellationToken _) =>
            Task.FromResult(false);
        public Task<string?> GetDefaultBranchAsync(CancellationToken _) =>
            Task.FromResult<string?>(null);
    }

    internal static async Task<int> RunAsync()
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Windows Named Pipe test requires Windows.");

        await CheckAsync("health", "ok", null);
        await CheckAsync("capabilities", "ok", null);
        await CheckAsync("powershell", "error", "unknown_action");
        Console.WriteLine("PASS: isolated Named Pipe current-user health, capabilities and command denial.");
        return 0;
    }

    private static async Task CheckAsync(string action, string status, string? error)
    {
        string pipeName = "uub.qa." + Guid.NewGuid().ToString("N");
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        Task serving = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(limit.Token);
            using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024,
                leaveOpen: true) { AutoFlush = true };
            string? request = await reader.ReadLineAsync(limit.Token);
            object response = await AgentProtocol.RunAsync(
                request ?? "", new NoNetworkReader(), limit.Token);
            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        }, limit.Token);

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(5000, limit.Token);
        using var input = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true);
        using var output = new StreamWriter(client, new UTF8Encoding(false), 1024,
            leaveOpen: true) { AutoFlush = true };
        await output.WriteLineAsync(JsonSerializer.Serialize(new { action }));
        string? reply = await input.ReadLineAsync(limit.Token);
        await serving;

        using var parsed = JsonDocument.Parse(reply ?? throw new IOException("Missing IPC reply."));
        if (parsed.RootElement.GetProperty("status").GetString() != status)
            throw new InvalidDataException("Unexpected IPC status for " + action);
        if (error is not null && parsed.RootElement.GetProperty("error").GetString() != error)
            throw new InvalidDataException("Unapproved IPC action was not denied.");
        if (action == "health")
        {
            var health = parsed.RootElement;
            if (health.GetProperty("service").GetString() != "uUnityBridge.Agent" ||
                health.GetProperty("adminToolsEnabled").GetBoolean() ||
                health.GetProperty("transport").GetString() != "current-user-named-pipe")
                throw new InvalidDataException("IPC health response missing safety boundary.");
        }
    }
}
