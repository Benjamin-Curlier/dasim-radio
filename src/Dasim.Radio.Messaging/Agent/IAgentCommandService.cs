using Dasim.Radio.Contracts;

namespace Dasim.Radio.Messaging.Agent;

/// <summary>
/// Client side (manager): invokes the <c>agent.&lt;host&gt;.cmd</c> NATS service to launch, stop
/// or reconfigure a post.
/// </summary>
public interface IAgentCommandClient
{
    /// <summary>Sends a raw command envelope to a host and awaits its reply.</summary>
    ValueTask<AgentCommandResult> SendAsync(string hostId, AgentCommandEnvelope command, CancellationToken cancellationToken = default);

    /// <summary>Asks the host to launch its client with the given configuration.</summary>
    ValueTask<AgentCommandResult> LaunchAsync(string hostId, string configId, CancellationToken cancellationToken = default);

    /// <summary>Asks the host to stop its running client.</summary>
    ValueTask<AgentCommandResult> StopAsync(string hostId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Server side (agent host): exposes the <c>agent.&lt;host&gt;.cmd</c> NATS service for one host.
/// Dispose the returned handle to stop serving.
/// </summary>
public interface IAgentCommandServer
{
    /// <summary>
    /// Registers the command service for <paramref name="hostId"/>; <paramref name="handler"/> is
    /// invoked for each request and returns the reply.
    /// </summary>
    ValueTask<IAsyncDisposable> StartAsync(
        string hostId,
        Func<AgentCommandEnvelope, CancellationToken, ValueTask<AgentCommandResult>> handler,
        CancellationToken cancellationToken = default);
}
