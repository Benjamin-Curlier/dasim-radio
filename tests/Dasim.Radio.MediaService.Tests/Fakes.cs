using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Dasim.Radio.Contracts;
using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Floor;
using Dasim.Radio.MediaService.Routing;
using Dasim.Radio.Messaging.Audio;
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

/// <summary>An <see cref="IForceTreeProvider"/> that returns a fixed (settable) routing snapshot.</summary>
internal sealed class FakeForceTreeProvider(ForceRouting routing) : IForceTreeProvider
{
    public ForceRouting Current { get; set; } = routing;
}

/// <summary>An <see cref="IFloorHolders"/> that returns a fixed (settable) holders snapshot.</summary>
internal sealed class FakeFloorHolders(FloorHolders holders) : IFloorHolders
{
    public FloorHolders Holders { get; set; } = holders;

    public FloorHolders Current() => Holders;
}

/// <summary>
/// A <see cref="INatsKeyValueStore{T}"/> for the force tree that replays scripted watch entries, then
/// stays "watching" until cancelled. Lets <see cref="ForceTreeProvider"/> be driven without a server.
/// </summary>
internal sealed class ScriptedForceTreeBucket(IReadOnlyList<KeyValueEntry<ForceTreeDto>> entries)
    : INatsKeyValueStore<ForceTreeDto>
{
    public string Bucket => Subjects.Buckets.ForceTree;

    public async IAsyncEnumerable<KeyValueEntry<ForceTreeDto>> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (KeyValueEntry<ForceTreeDto> entry in entries)
        {
            yield return entry;
        }

        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<ulong> PutAsync(string key, ForceTreeDto value, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<ulong> CreateAsync(string key, ForceTreeDto value, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<ulong> UpdateAsync(string key, ForceTreeDto value, ulong revision, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<KeyValueEntry<ForceTreeDto>?> TryGetAsync(string key, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<string> GetKeysAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>An <see cref="IControlPlaneStore"/> that hands out a given force-tree bucket; the rest is unused.</summary>
internal sealed class ForceTreeControlPlaneStore(INatsKeyValueStore<ForceTreeDto> forceTree) : IControlPlaneStore
{
    public ValueTask<INatsKeyValueStore<ForceTreeDto>> ForceTreeAsync(CancellationToken cancellationToken = default) =>
        new(forceTree);

    public ValueTask<INatsKeyValueStore<EndpointDto>> EndpointsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<INatsKeyValueStore<PresenceHeartbeat>> PresenceAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<INatsKeyValueStore<FloorStateDto>> FloorStateAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<INatsKeyValueStore<T>> BucketAsync<T>(string bucket, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>
/// An <see cref="IAudioBus"/> that replays scripted captured frames (then idles until cancelled) and
/// records every mixed frame published back. Drives <see cref="MediaRouterService"/> without a server.
/// </summary>
internal sealed class ScriptedAudioBus(IReadOnlyList<AudioFrame> captured) : IAudioBus
{
    public ConcurrentQueue<(string ClientId, byte[] Opus)> Published { get; } = new();

    public ValueTask PublishMixedAsync(
        string listenerClientId, ReadOnlyMemory<byte> opusFrame, CancellationToken cancellationToken = default)
    {
        Published.Enqueue((listenerClientId, opusFrame.ToArray()));
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<AudioFrame> SubscribeCapturedAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (AudioFrame frame in captured)
        {
            yield return frame;
        }

        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask PublishCapturedAsync(
        string clientId, ReadOnlyMemory<byte> opusFrame, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<byte[]> SubscribeMixedAsync(string clientId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
