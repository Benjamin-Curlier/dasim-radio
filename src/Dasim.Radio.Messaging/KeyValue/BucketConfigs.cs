using Dasim.Radio.Contracts;
using NATS.Client.KeyValueStore;

namespace Dasim.Radio.Messaging.KeyValue;

/// <summary>Documented per-bucket configuration for the control-plane KV buckets.</summary>
internal static class BucketConfigs
{
    /// <summary>Stale heartbeats expire so a crashed post disappears from presence.</summary>
    public static readonly TimeSpan PresenceTtl = ControlPlaneTtls.Presence;

    /// <summary>Revisions of the force tree kept for audit/rollback.</summary>
    public const long ForceTreeHistory = 5;

    public static NatsKVConfig For(string bucket) => bucket switch
    {
        Subjects.Buckets.ForceTree => new NatsKVConfig(bucket) { History = ForceTreeHistory },
        Subjects.Buckets.Presence => new NatsKVConfig(bucket) { MaxAge = PresenceTtl },
        _ => new NatsKVConfig(bucket),
    };
}
