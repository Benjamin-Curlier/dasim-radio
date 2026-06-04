using Dasim.Radio.Contracts;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

namespace Dasim.Radio.Messaging.KeyValue;

/// <summary>JetStream KV implementation of <see cref="IControlPlaneStore"/>.</summary>
public sealed class NatsControlPlaneStore : IControlPlaneStore
{
    private readonly NatsKVContext _kv;

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
        INatsKVStore store = await _kv.CreateOrUpdateStoreAsync(BucketConfigs.For(bucket), cancellationToken);
        return new NatsKeyValueStore<T>(store);
    }
}
