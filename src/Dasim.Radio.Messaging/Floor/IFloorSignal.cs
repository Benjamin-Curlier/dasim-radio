using Dasim.Radio.Contracts;

namespace Dasim.Radio.Messaging.Floor;

/// <summary>
/// Floor (push-to-talk) signalling over core NATS. Clients publish requests/releases and
/// observe per-net events; the media service consumes requests/releases, runs the authoritative
/// <c>FloorControlService</c>, and broadcasts the resulting <see cref="FloorEventMessage"/>.
/// This is signalling only — the arbitration decision lives in <c>Dasim.Radio.Core</c>.
/// </summary>
public interface IFloorSignal
{
    /// <summary>Publishes a push-to-talk request to <c>floor.request</c>.</summary>
    ValueTask RequestAsync(FloorRequestMessage request, CancellationToken cancellationToken = default);

    /// <summary>Publishes a push-to-talk release to <c>floor.release</c>.</summary>
    ValueTask ReleaseAsync(FloorReleaseMessage release, CancellationToken cancellationToken = default);

    /// <summary>Broadcasts a floor decision to <c>floor.events.&lt;netId&gt;</c> — media service side.</summary>
    ValueTask PublishEventAsync(FloorEventMessage @event, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to all push-to-talk requests — media service side.</summary>
    IAsyncEnumerable<FloorRequestMessage> SubscribeRequestsAsync(CancellationToken cancellationToken = default);

    /// <summary>Subscribes to all push-to-talk releases — media service side.</summary>
    IAsyncEnumerable<FloorReleaseMessage> SubscribeReleasesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to floor decisions for one net — client side. <paramref name="onSubscribed"/>, if
    /// supplied, is invoked once the subscription is live on the server, so a caller can wait for
    /// registration before publishing a request whose grant rides the same un-replayed core-NATS subject
    /// (otherwise an early grant can be dropped and the client stranded in Requesting).
    /// </summary>
    IAsyncEnumerable<FloorEventMessage> SubscribeEventsAsync(
        string netId, Action? onSubscribed = null, CancellationToken cancellationToken = default);
}
