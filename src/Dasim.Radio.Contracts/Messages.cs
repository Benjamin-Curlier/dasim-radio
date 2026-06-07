namespace Dasim.Radio.Contracts;

// Wire contracts exchanged over NATS. Deliberately use primitive types only so they
// serialize cleanly and stay decoupled from the domain model in Dasim.Radio.Core.

/// <summary>
/// Push-to-talk pressed: request to transmit on a net. <see cref="Sequence"/> is a per-participant
/// monotonic press counter set on PTT-down; the matching release carries the same value, letting the
/// authority reject a stale release that a transport reorder placed after the holder's re-grant.
/// <c>0</c> means unsequenced (no reorder protection — the legacy/default value).
/// </summary>
public sealed record FloorRequestMessage(string NetId, string ParticipantId, int Priority, long Sequence = 0);

/// <summary>
/// Push-to-talk released: stop transmitting on a net. <see cref="Sequence"/> echoes the
/// <see cref="FloorRequestMessage.Sequence"/> of the press being released, so the authority can ignore
/// a release that arrives (out of order) after the same participant has already re-acquired the floor.
/// </summary>
public sealed record FloorReleaseMessage(string NetId, string ParticipantId, long Sequence = 0);

/// <summary>
/// A floor decision broadcast to interested clients. <see cref="CurrentHolder"/> carries the net's
/// resulting holder (null when idle) so a client that joined late or dropped a packet can render the
/// current state from the event alone, without reading the eventually-consistent <c>floor_state</c>.
/// </summary>
public sealed record FloorEventMessage(
    string NetId, string Outcome, string Requester, string? Preempted, string? CurrentHolder = null);

/// <summary>The wire values used in <see cref="FloorEventMessage.Outcome"/>.</summary>
public static class FloorOutcomes
{
    /// <summary>Push-to-talk granted on an idle net.</summary>
    public const string Granted = "granted";

    /// <summary>Push-to-talk granted by pre-empting a lower-priority holder (see <see cref="FloorEventMessage.Preempted"/>).</summary>
    public const string GrantedWithPreemption = "granted_preemption";

    /// <summary>Push-to-talk denied: the floor is held at equal or higher priority.</summary>
    public const string Denied = "denied";

    /// <summary>The holder released the floor; the net is now idle.</summary>
    public const string Released = "released";
}

/// <summary>Periodic heartbeat from a host agent so the manager can discover posts.</summary>
public sealed record PresenceHeartbeat(
    string HostId,
    string HostName,
    string IpAddress,
    string? ClientId,
    DateTimeOffset TimestampUtc);

/// <summary>Manager -&gt; agent: launch the client with a configuration.</summary>
public sealed record LaunchClientCommand(string HostId, string ConfigId);

/// <summary>Manager -&gt; agent: stop the running client.</summary>
public sealed record StopClientCommand(string HostId);

/// <summary>
/// Request payload for the <c>agent.&lt;host&gt;.cmd</c> NATS service. The target host is
/// encoded in the subject, so only the verb and its arguments travel in the body.
/// <see cref="Kind"/> is one of <see cref="AgentCommandKinds"/>.
/// </summary>
public sealed record AgentCommandEnvelope(string Kind, string? ConfigId = null);

/// <summary>Reply payload for the <c>agent.&lt;host&gt;.cmd</c> NATS service.</summary>
public sealed record AgentCommandResult(bool Accepted, string? Error = null);

/// <summary>The verbs understood by the <c>agent.&lt;host&gt;.cmd</c> service.</summary>
public static class AgentCommandKinds
{
    public const string Launch = "launch";
    public const string Stop = "stop";
    public const string Reconfigure = "reconfigure";
}

/// <summary>
/// Manager/app -&gt; media service: degrade a specific listener's reception
/// (quality and clarity expressed as 0-100 percentages), optionally scoped to one net.
/// </summary>
public sealed record DegradeCommand(
    string TargetClientId,
    string? NetId,
    int QualityPercent,
    int ClarityPercent);
