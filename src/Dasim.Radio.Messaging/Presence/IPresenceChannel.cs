using Dasim.Radio.Contracts;

namespace Dasim.Radio.Messaging.Presence;

/// <summary>
/// Presence heartbeats over core NATS (<c>presence.heartbeat</c>). Agents broadcast so the
/// manager can discover live posts. The durable, TTL'd view lives in the <c>presence</c> KV
/// bucket (see <see cref="IControlPlaneStore"/>); this channel is the low-latency fan-out.
/// </summary>
public interface IPresenceChannel
{
    /// <summary>Broadcasts a heartbeat — agent side.</summary>
    ValueTask PublishAsync(PresenceHeartbeat heartbeat, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to all heartbeats — manager side.</summary>
    IAsyncEnumerable<PresenceHeartbeat> SubscribeAsync(CancellationToken cancellationToken = default);
}
