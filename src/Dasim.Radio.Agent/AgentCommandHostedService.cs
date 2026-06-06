using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.Agent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dasim.Radio.Agent;

/// <summary>
/// Serves the <c>agent.&lt;host&gt;.cmd</c> NATS service for this host and dispatches each command to the
/// <see cref="IClientController"/>. Not a <see cref="BackgroundService"/> — there is nothing to loop;
/// it just holds the service handle open between start and stop.
/// </summary>
public sealed class AgentCommandHostedService : IHostedService
{
    private readonly IAgentCommandServer _server;
    private readonly IClientController _controller;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentCommandHostedService> _logger;

    private IAsyncDisposable? _handle;

    public AgentCommandHostedService(
        IAgentCommandServer server,
        IClientController controller,
        IOptions<AgentOptions> options,
        ILogger<AgentCommandHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _server = server;
        _controller = controller;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _handle = await _server.StartAsync(_options.HostId, DispatchAsync, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Agent command service listening on agent.{HostId}.cmd.", _options.HostId);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_handle is not null)
        {
            await _handle.DisposeAsync().ConfigureAwait(false);
            _handle = null;
        }
    }

    // Runs on the NATS service's handler path. It must NEVER throw: an escaping exception would fault the
    // reply and leave the manager waiting for a timeout, so every failure is mapped to a declined result.
    private async ValueTask<AgentCommandResult> DispatchAsync(AgentCommandEnvelope command, CancellationToken cancellationToken)
    {
        // Match the exact lowercase wire constants (AgentCommandKinds) — the only producer is the
        // manager via NatsAgentCommandClient, so strict matching keeps the protocol unambiguous.
        try
        {
            if (string.Equals(command.Kind, AgentCommandKinds.Launch, StringComparison.Ordinal))
            {
                return await WithConfigAsync(command, _controller.LaunchAsync, cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(command.Kind, AgentCommandKinds.Stop, StringComparison.Ordinal))
            {
                return await _controller.StopAsync(cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(command.Kind, AgentCommandKinds.Reconfigure, StringComparison.Ordinal))
            {
                return await WithConfigAsync(command, _controller.ReconfigureAsync, cancellationToken).ConfigureAwait(false);
            }

            return new AgentCommandResult(false, $"Unknown command kind '{command.Kind}'.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Agent command '{Kind}' failed.", command.Kind);
            return new AgentCommandResult(false, ex.Message);
        }
    }

    private static async ValueTask<AgentCommandResult> WithConfigAsync(
        AgentCommandEnvelope command,
        Func<string, CancellationToken, ValueTask<AgentCommandResult>> action,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.ConfigId))
        {
            return new AgentCommandResult(false, $"Command '{command.Kind}' requires a configId.");
        }

        return await action(command.ConfigId, cancellationToken).ConfigureAwait(false);
    }
}
