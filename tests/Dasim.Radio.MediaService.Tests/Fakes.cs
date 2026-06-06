using Dasim.Radio.Contracts;
using Dasim.Radio.MediaService.Floor;
using Dasim.Radio.Messaging.Floor;

namespace Dasim.Radio.MediaService.Tests;

/// <summary>An <see cref="IFloorSignal"/> that records broadcast events; the rest is unused by the arbiter.</summary>
internal sealed class RecordingFloorSignal : IFloorSignal
{
    public List<FloorEventMessage> Published { get; } = [];

    public ValueTask PublishEventAsync(FloorEventMessage @event, CancellationToken cancellationToken = default)
    {
        Published.Add(@event);
        return ValueTask.CompletedTask;
    }

    public ValueTask RequestAsync(FloorRequestMessage request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask ReleaseAsync(FloorReleaseMessage release, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<FloorRequestMessage> SubscribeRequestsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<FloorReleaseMessage> SubscribeReleasesAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<FloorEventMessage> SubscribeEventsAsync(string netId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>An <see cref="IFloorStateWriter"/> that records persisted floor state.</summary>
internal sealed class RecordingFloorStateWriter : IFloorStateWriter
{
    public List<FloorStateDto> Written { get; } = [];

    public ValueTask WriteAsync(FloorStateDto state, CancellationToken cancellationToken = default)
    {
        Written.Add(state);
        return ValueTask.CompletedTask;
    }
}

/// <summary>An <see cref="IFloorStateWriter"/> whose persistence always fails (KV unavailable).</summary>
internal sealed class ThrowingFloorStateWriter : IFloorStateWriter
{
    public ValueTask WriteAsync(FloorStateDto state, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("floor_state bucket unavailable.");
}

/// <summary>
/// A resolver that maps each participant to a fixed priority, ignoring the wire value. Lets a test
/// prove the arbiter arbitrates on the RESOLVED priority rather than what the client claimed.
/// </summary>
internal sealed class MappingPriorityResolver(Dictionary<string, int> byParticipant) : IFloorPriorityResolver
{
    public ValueTask<Core.Priority> ResolveAsync(
        Core.ParticipantId participant, Core.Priority requested, CancellationToken cancellationToken = default) =>
        new(new Core.Priority(byParticipant.TryGetValue(participant.Value, out int p) ? p : requested.Value));
}
