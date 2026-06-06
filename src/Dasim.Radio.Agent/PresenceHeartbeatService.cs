using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.KeyValue;
using Dasim.Radio.Messaging.Presence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dasim.Radio.Agent;

/// <summary>
/// Broadcasts presence so the manager can discover this post. Each beat is published twice: on the
/// core-NATS <see cref="IPresenceChannel"/> (low-latency fan-out for a late-joining manager) and into
/// the <c>presence</c> KV bucket (the durable, TTL'd snapshot). Re-writing the KV every beat refreshes
/// the 15s TTL, so a crashed post disappears on its own; a clean stop deletes the key immediately.
/// </summary>
public sealed class PresenceHeartbeatService : BackgroundService
{
    private readonly IPresenceChannel _channel;
    private readonly IControlPlaneStore _store;
    private readonly IClientController _controller;
    private readonly AgentOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PresenceHeartbeatService> _logger;

    private INatsKeyValueStore<PresenceHeartbeat>? _bucket;

    public PresenceHeartbeatService(
        IPresenceChannel channel,
        IControlPlaneStore store,
        IClientController controller,
        IOptions<AgentOptions> options,
        TimeProvider timeProvider,
        ILogger<PresenceHeartbeatService> logger)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _channel = channel;
        _store = store;
        _controller = controller;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Presence heartbeat started for host '{HostId}' every {Interval}.", _options.HostId, _options.HeartbeatInterval);

        // Beat once immediately so a freshly-booted post is discoverable without waiting a full interval.
        await SendHeartbeatAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(_options.HeartbeatInterval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await SendHeartbeatAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // Deliberate stop: remove the presence key now rather than letting the TTL lapse (~15s) so the
        // manager sees the post go offline immediately. Best-effort — a crash falls back to the TTL.
        // Only attempt this if we ever bound the bucket: if the broker was down all run, binding here
        // would block the shutdown grace period just to delete a key we never wrote.
        if (_bucket is null)
        {
            return;
        }

        try
        {
            await _bucket.DeleteAsync(_options.HostId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to deregister presence for host '{HostId}'.", _options.HostId);
        }
    }

    private async ValueTask SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        PresenceHeartbeat heartbeat = BuildHeartbeat();

        try
        {
            await _channel.PublishAsync(heartbeat, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to broadcast presence heartbeat for host '{HostId}'.", _options.HostId);
        }

        try
        {
            _bucket ??= await _store.PresenceAsync(cancellationToken).ConfigureAwait(false);
            await _bucket.PutAsync(_options.HostId, heartbeat, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to persist presence heartbeat for host '{HostId}'.", _options.HostId);
            _bucket = null; // Force a re-bind on the next beat in case the bucket handle went bad.
        }
    }

    private PresenceHeartbeat BuildHeartbeat() => new(
        _options.HostId,
        _options.HostName,
        _options.IpAddress,
        // ClientId currently carries the launched configId as a stand-in for the client's real audio
        // client-id, which the agent will only learn once the client host reports it (a follow-up).
        _controller.CurrentConfigId,
        _timeProvider.GetUtcNow());
}
