using Dasim.Radio.Contracts;
using NATS.Client.Core;

namespace Dasim.Radio.Messaging.Agent;

/// <summary>NATS request/reply implementation of <see cref="IAgentCommandClient"/>.</summary>
public sealed class NatsAgentCommandClient : IAgentCommandClient
{
    private readonly INatsConnection _connection;

    public NatsAgentCommandClient(INatsConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    public async ValueTask<AgentCommandResult> SendAsync(string hostId, AgentCommandEnvelope command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        ArgumentNullException.ThrowIfNull(command);

        NatsMsg<AgentCommandResult> reply = await _connection.RequestAsync<AgentCommandEnvelope, AgentCommandResult>(
            Subjects.Control.AgentCommand(hostId), command, cancellationToken: cancellationToken);

        return reply.Data ?? new AgentCommandResult(false, "No response from agent.");
    }

    public ValueTask<AgentCommandResult> LaunchAsync(string hostId, string configId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configId);
        return SendAsync(hostId, new AgentCommandEnvelope(AgentCommandKinds.Launch, configId), cancellationToken);
    }

    public ValueTask<AgentCommandResult> StopAsync(string hostId, CancellationToken cancellationToken = default) =>
        SendAsync(hostId, new AgentCommandEnvelope(AgentCommandKinds.Stop), cancellationToken);
}
