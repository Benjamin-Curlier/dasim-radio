using Dasim.Radio.Contracts;

namespace Dasim.Radio.Messaging.KeyValue;

/// <summary>
/// Typed access to the control-plane JetStream KV buckets (see <see cref="Subjects.Buckets"/>).
/// Each bucket is created on first access with its documented configuration (TTL, history).
/// Callers should obtain a bucket once and reuse it rather than calling these per operation.
/// </summary>
public interface IControlPlaneStore
{
    /// <summary>The <c>force_tree</c> bucket (versioned hierarchy).</summary>
    ValueTask<INatsKeyValueStore<ForceTreeDto>> ForceTreeAsync(CancellationToken cancellationToken = default);

    /// <summary>The <c>endpoints</c> bucket (post ↔ host).</summary>
    ValueTask<INatsKeyValueStore<EndpointDto>> EndpointsAsync(CancellationToken cancellationToken = default);

    /// <summary>The <c>presence</c> bucket (heartbeats with TTL).</summary>
    ValueTask<INatsKeyValueStore<PresenceHeartbeat>> PresenceAsync(CancellationToken cancellationToken = default);

    /// <summary>The <c>floor_state</c> bucket (authoritative floor holder per net).</summary>
    ValueTask<INatsKeyValueStore<FloorStateDto>> FloorStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Binds an arbitrary bucket (e.g. <c>configs</c>) to a value type.</summary>
    ValueTask<INatsKeyValueStore<T>> BucketAsync<T>(string bucket, CancellationToken cancellationToken = default);
}
