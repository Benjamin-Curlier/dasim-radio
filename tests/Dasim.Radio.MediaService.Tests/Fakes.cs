using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Dasim.Radio.Contracts;
using Dasim.Radio.MediaService.Floor;
using Dasim.Radio.Messaging.Floor;
using Dasim.Radio.Messaging.KeyValue;

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
/// An <see cref="IFloorSignal"/> that replays scripted request/release streams (then stays
/// "subscribed" until cancelled) and records the events the arbiter broadcasts back through it —
/// lets <see cref="FloorAuthorityService"/> be driven end-to-end without a NATS server.
/// </summary>
internal sealed class ScriptedFloorSignal(
    IReadOnlyList<FloorRequestMessage> requests, IReadOnlyList<FloorReleaseMessage> releases) : IFloorSignal
{
    public ConcurrentQueue<FloorEventMessage> Published { get; } = new();

    public ValueTask PublishEventAsync(FloorEventMessage @event, CancellationToken cancellationToken = default)
    {
        Published.Enqueue(@event);
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<FloorRequestMessage> SubscribeRequestsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (FloorRequestMessage request in requests)
        {
            yield return request;
        }

        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<FloorReleaseMessage> SubscribeReleasesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (FloorReleaseMessage release in releases)
        {
            yield return release;
        }

        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
    }

    ValueTask IFloorSignal.RequestAsync(FloorRequestMessage request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    ValueTask IFloorSignal.ReleaseAsync(FloorReleaseMessage release, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    IAsyncEnumerable<FloorEventMessage> IFloorSignal.SubscribeEventsAsync(string netId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>A <see cref="INatsKeyValueStore{T}"/> for floor state that records <c>Put</c>s; the rest is unused.</summary>
internal sealed class FakeFloorStateBucket : INatsKeyValueStore<FloorStateDto>
{
    public List<(string Key, FloorStateDto Value)> Puts { get; } = [];

    public string Bucket => Subjects.Buckets.FloorState;

    public ValueTask<ulong> PutAsync(string key, FloorStateDto value, CancellationToken cancellationToken = default)
    {
        Puts.Add((key, value));
        return ValueTask.FromResult((ulong)Puts.Count);
    }

    public ValueTask<ulong> CreateAsync(string key, FloorStateDto value, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<ulong> UpdateAsync(string key, FloorStateDto value, ulong revision, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<KeyValueEntry<FloorStateDto>?> TryGetAsync(string key, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<string> GetKeysAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<KeyValueEntry<FloorStateDto>> WatchAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>An <see cref="IControlPlaneStore"/> that hands out a given floor-state bucket; the rest is unused.</summary>
internal sealed class FakeControlPlaneStore(INatsKeyValueStore<FloorStateDto> floorState) : IControlPlaneStore
{
    public ValueTask<INatsKeyValueStore<FloorStateDto>> FloorStateAsync(CancellationToken cancellationToken = default) =>
        new(floorState);

    public ValueTask<INatsKeyValueStore<ForceTreeDto>> ForceTreeAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<INatsKeyValueStore<EndpointDto>> EndpointsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<INatsKeyValueStore<PresenceHeartbeat>> PresenceAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<INatsKeyValueStore<T>> BucketAsync<T>(string bucket, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
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
