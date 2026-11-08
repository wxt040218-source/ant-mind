using AntMind.Contracts;
using AntMind.Transport;
using Microsoft.Extensions.Hosting;

// STDIO is one SDK session. A completed initialize handshake starts its host lease.
internal sealed class AgentLink(PipeClient client) : BackgroundService {
    private readonly Guid session = Guid.NewGuid();
    private string? clientName;
    public async Task InitializeAsync(string name, CancellationToken ct) {
        Volatile.Write(ref clientName, string.IsNullOrWhiteSpace(name) ? "MCP Agent" : name[..Math.Min(80, name.Length)]);
        await Pulse(ct);
    }
    private async Task Pulse(CancellationToken ct, bool connected = true) {
        var name = Volatile.Read(ref clientName);
        if (name is null) return;
        try { await client.SendAsync(new(Protocol.Version, "agent_presence", Agent: new(session, name, connected)), ct); }
        catch (Exception error) when (error is IOException or OperationCanceledException or TimeoutException or UnauthorizedAccessException) {
            // A missing desktop host cannot turn the indicator green. Retry at the next heartbeat.
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try { while (await timer.WaitForNextTickAsync(stoppingToken)) await Pulse(stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    public override async Task StopAsync(CancellationToken ct) {
        await Pulse(ct, false);
        await base.StopAsync(ct);
    }
}
