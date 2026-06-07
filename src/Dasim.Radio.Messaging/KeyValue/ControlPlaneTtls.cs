namespace Dasim.Radio.Messaging.KeyValue;

/// <summary>
/// Public TTLs for the control-plane KV buckets, so a writer (e.g. the agent's presence heartbeat) can
/// validate its cadence against the same value the bucket is created with, instead of duplicating the
/// literal. The bucket configuration (<see cref="BucketConfigs"/>) is the single consumer on the create
/// side; this is the single source of truth.
/// </summary>
public static class ControlPlaneTtls
{
    /// <summary>
    /// Presence heartbeats expire after this so a crashed post disappears from presence. A writer must
    /// refresh its key at least twice per window (interval &lt;= half this) or a live post can flicker
    /// offline when a single beat is late or dropped.
    /// </summary>
    public static readonly TimeSpan Presence = TimeSpan.FromSeconds(15);
}
