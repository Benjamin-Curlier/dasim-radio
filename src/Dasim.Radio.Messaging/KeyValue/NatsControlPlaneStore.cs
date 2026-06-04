using System.Collections.Concurrent;
using Dasim.Radio.Contracts;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

namespace Dasim.Radio.Messaging.KeyValue;

/// <summary>JetStream KV implementation of <see cref="IControlPlaneStore"/>.</summary>
public sealed class NatsControlPlaneStore : IControlPlaneStore
{
    private readonly NatsKVContext _kv;

    // Bind (create-or-update) each bucket once and share it: accessor calls are then a cheap
    // dictionary hit plus a typed wrapper, not a JetStream round-trip.
    private readonly ConcurrentDictionary<string, Task<INatsKVStore>> _bindings = new(StringComparer.Ordinal);

    public NatsControlPlaneStore(INatsConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _kv = new NatsKVContext(new NatsJSContext(connection));
    }

    public ValueTask<INatsKeyValueStore<ForceTreeDto>> ForceTreeAsync(CancellationToken cancellationToken = default) =>
        BucketAsync<ForceTreeDto>(Subjects.Buckets.ForceTree, cancellationToken);

    public ValueTask<INatsKeyValueStore<EndpointDto>> EndpointsAsync(CancellationToken cancellationToken = default) =>
        BucketAsync<EndpointDto>(Subjects.Buckets.Endpoints, cancellationToken);

    public ValueTask<INatsKeyValueStore<PresenceHeartbeat>> PresenceAsync(CancellationToken cancellationToken = default) =>
        BucketAsync<PresenceHeartbeat>(Subjects.Buckets.Presence, cancellationToken);

    public ValueTask<INatsKeyValueStore<FloorStateDto>> FloorStateAsync(CancellationToken cancellationToken = default) =>
        BucketAsync<FloorStateDto>(Subjects.Buckets.FloorState, cancellationToken);

    public async ValueTask<INatsKeyValueStore<T>> BucketAsync<T>(string bucket, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucket);

        Task<INatsKVStore> binding = _bindings.GetOrAdd(bucket, Bind);
        try
        {
            // Each caller waits on its own token; the shared bind runs once.
            INatsKVStore store = await binding.WaitAsync(cancellationToken);
            return new NatsKeyValueStore<T>(store);
        }
        catch
        {
            // Never cache a failed bind — let the next caller retry.
            _bindings.TryRemove(new KeyValuePair<string, Task<INatsKVStore>>(bucket, binding));
            throw;
        }
    }

    private Task<INatsKVStore> Bind(string bucket) =>
        _kv.CreateOrUpdateStoreAsync(BucketConfigs.For(bucket)).AsTask();
}
