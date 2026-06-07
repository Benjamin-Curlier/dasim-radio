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

        try
        {
            NatsMsg<AgentCommandResult> reply = await _connection.RequestAsync<AgentCommandEnvelope, AgentCommandResult>(
                Subjects.Control.AgentCommand(hostId), command, cancellationToken: cancellationToken);

            return reply.Data ?? new AgentCommandResult(false, "No response from agent.");
        }
        catch (NatsNoRespondersException)
        {
            // No agent service is listening on this host's subject (the post is offline) — NATS returns a
            // no-responders status that surfaces here as an exception. Return the same graceful
            // 'not accepted' result as a null-bodied reply so the contract is uniform whether the host is
            // silent or absent, instead of making every caller guard this case.
            return new AgentCommandResult(false, "No response from agent.");
        }
    }

    public ValueTask<AgentCommandResult> LaunchAsync(string hostId, string configId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configId);
        return SendAsync(hostId, new AgentCommandEnvelope(AgentCommandKinds.Launch, configId), cancellationToken);
    }

    public ValueTask<AgentCommandResult> StopAsync(string hostId, CancellationToken cancellationToken = default) =>
        SendAsync(hostId, new AgentCommandEnvelope(AgentCommandKinds.Stop), cancellationToken);
}
