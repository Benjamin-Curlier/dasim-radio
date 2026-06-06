using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.Agent;
using Microsoft.Extensions.Logging;

namespace Dasim.Radio.Manager.Core;

/// <summary>Commands a host agent to launch/stop/reconfigure its client, via the agent NATS service.</summary>
public interface IPostControlService
{
    /// <summary>Launches the client on <paramref name="hostId"/> with <paramref name="configId"/>.</summary>
    ValueTask<AgentCommandResult> LaunchAsync(string hostId, string configId, CancellationToken cancellationToken = default);

    /// <summary>Stops the client on <paramref name="hostId"/>.</summary>
    ValueTask<AgentCommandResult> StopAsync(string hostId, CancellationToken cancellationToken = default);

    /// <summary>Reconfigures the client on <paramref name="hostId"/> to <paramref name="configId"/>.</summary>
    ValueTask<AgentCommandResult> ReconfigureAsync(string hostId, string configId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of <see cref="IPostControlService"/>. Pre-checks that the referenced config exists so the
/// manager UI gets a fast, clear error rather than waiting for the agent to decline an unknown config.
/// </summary>
public sealed class PostControlService(
    IAgentCommandClient agentClient, IClientConfigService configs, ILogger<PostControlService> logger)
    : IPostControlService
{
    private readonly IAgentCommandClient _agentClient = agentClient ?? throw new ArgumentNullException(nameof(agentClient));
    private readonly IClientConfigService _configs = configs ?? throw new ArgumentNullException(nameof(configs));
    private readonly ILogger<PostControlService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async ValueTask<AgentCommandResult> LaunchAsync(
        string hostId, string configId, CancellationToken cancellationToken = default)
    {
        NatsToken.EnsureSingleToken(hostId, nameof(hostId));
        AgentCommandResult? rejected = await EnsureConfigExistsAsync(configId, cancellationToken).ConfigureAwait(false);
        if (rejected is { } reject)
        {
            return reject;
        }

        _logger.LogInformation("Launching '{ConfigId}' on host '{HostId}'.", configId, hostId);
        return await _agentClient.LaunchAsync(hostId, configId, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<AgentCommandResult> StopAsync(string hostId, CancellationToken cancellationToken = default)
    {
        NatsToken.EnsureSingleToken(hostId, nameof(hostId));
        _logger.LogInformation("Stopping the client on host '{HostId}'.", hostId);
        return _agentClient.StopAsync(hostId, cancellationToken);
    }

    public async ValueTask<AgentCommandResult> ReconfigureAsync(
        string hostId, string configId, CancellationToken cancellationToken = default)
    {
        NatsToken.EnsureSingleToken(hostId, nameof(hostId));
        AgentCommandResult? rejected = await EnsureConfigExistsAsync(configId, cancellationToken).ConfigureAwait(false);
        if (rejected is { } reject)
        {
            return reject;
        }

        _logger.LogInformation("Reconfiguring host '{HostId}' to '{ConfigId}'.", hostId, configId);
        return await _agentClient.SendAsync(
            hostId, new AgentCommandEnvelope(AgentCommandKinds.Reconfigure, configId), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<AgentCommandResult?> EnsureConfigExistsAsync(string configId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configId);
        ClientConfigEntry? config = await _configs.GetAsync(configId, cancellationToken).ConfigureAwait(false);
        return config is null ? new AgentCommandResult(false, $"Unknown config '{configId}'.") : null;
    }
}
