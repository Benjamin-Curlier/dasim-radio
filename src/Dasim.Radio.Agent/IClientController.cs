using Dasim.Radio.Contracts;

namespace Dasim.Radio.Agent;

/// <summary>
/// Owns the lifecycle of this host's single client process. Launch/stop/reconfigure are serialized so
/// overlapping commands (and the presence heartbeat's reads) never observe a torn state.
/// </summary>
public interface IClientController
{
    /// <summary>Whether a client process is currently running.</summary>
    bool IsRunning { get; }

    /// <summary>The configuration id of the running client, or <c>null</c> when none is running.</summary>
    string? CurrentConfigId { get; }

    /// <summary>Launches the client with <paramref name="configId"/>.</summary>
    ValueTask<AgentCommandResult> LaunchAsync(string configId, CancellationToken cancellationToken = default);

    /// <summary>Stops the running client (idempotent — succeeds when nothing is running).</summary>
    ValueTask<AgentCommandResult> StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops any running client and relaunches with <paramref name="configId"/>, atomically.</summary>
    ValueTask<AgentCommandResult> ReconfigureAsync(string configId, CancellationToken cancellationToken = default);
}
