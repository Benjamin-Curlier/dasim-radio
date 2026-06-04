using Dasim.Radio.Contracts;
using NATS.Client.Core;
using NATS.Client.Services;

namespace Dasim.Radio.Messaging.Agent;

/// <summary>NATS Services implementation of <see cref="IAgentCommandServer"/>.</summary>
public sealed class NatsAgentCommandServer : IAgentCommandServer
{
    // Service/endpoint names are NATS service metadata (for discovery), not wire subjects — the
    // bound subject is always Subjects.Control.AgentCommand(hostId) == "agent.<host>.cmd".
    private const string ServiceName = "agent";
    private const string ServiceVersion = "1.0.0";
    private const string EndpointName = "cmd";

    private readonly INatsConnection _connection;

    public NatsAgentCommandServer(INatsConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    public async ValueTask<IAsyncDisposable> StartAsync(
        string hostId,
        Func<AgentCommandEnvelope, CancellationToken, ValueTask<AgentCommandResult>> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        ArgumentNullException.ThrowIfNull(handler);

        var services = new NatsSvcContext(_connection);
        INatsSvcServer server = await services.AddServiceAsync(
            new NatsSvcConfig(ServiceName, ServiceVersion)
            {
                Description = $"Dasim.Radio agent commands for host '{hostId}'.",
            },
            cancellationToken);

        await server.AddEndpointAsync<AgentCommandEnvelope>(
            handler: async msg =>
            {
                if (msg.Data is null)
                {
                    await msg.ReplyErrorAsync(400, "Empty agent command.", cancellationToken: CancellationToken.None);
                    return;
                }

                AgentCommandResult result = await handler(msg.Data, cancellationToken);
                await msg.ReplyAsync(result, cancellationToken: CancellationToken.None);
            },
            name: EndpointName,
            subject: Subjects.Control.AgentCommand(hostId),
            cancellationToken: cancellationToken);

        return server;
    }
}
