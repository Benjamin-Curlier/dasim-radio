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

        // Handlers run for the whole life of the service, not just startup. They observe a token
        // tied to the returned handle, so it cancels on shutdown — never the caller's startup token.
        var lifetime = new CancellationTokenSource();
        INatsSvcServer? server = null;
        try
        {
            var services = new NatsSvcContext(_connection);
            server = await services.AddServiceAsync(
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
                        await msg.ReplyErrorAsync(400, "Empty agent command.", cancellationToken: lifetime.Token);
                        return;
                    }

                    AgentCommandResult result = await handler(msg.Data, lifetime.Token);
                    await msg.ReplyAsync(result, cancellationToken: lifetime.Token);
                },
                name: EndpointName,
                subject: Subjects.Control.AgentCommand(hostId),
                cancellationToken: cancellationToken);

            return new RunningService(server, lifetime);
        }
        catch
        {
            // AddServiceAsync already registered the $SRV.PING/INFO/STATS responders on the live
            // connection. If endpoint binding then fails we must tear that half-built service down, or
            // it keeps answering service-discovery pings on the shared connection with no owning handle.
            if (server is not null)
            {
                await server.DisposeAsync();
            }

            lifetime.Dispose();
            throw;
        }
    }

    // Cancels in-flight handlers, then tears the NATS service down.
    private sealed class RunningService(INatsSvcServer server, CancellationTokenSource lifetime) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await lifetime.CancelAsync();
            await server.DisposeAsync();
            lifetime.Dispose();
        }
    }
}
