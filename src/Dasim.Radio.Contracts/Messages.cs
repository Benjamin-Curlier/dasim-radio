namespace Dasim.Radio.Contracts;

// Wire contracts exchanged over NATS. Deliberately use primitive types only so they
// serialize cleanly and stay decoupled from the domain model in Dasim.Radio.Core.

/// <summary>Push-to-talk pressed: request to transmit on a net.</summary>
public sealed record FloorRequestMessage(string NetId, string ParticipantId, int Priority);

/// <summary>Push-to-talk released: stop transmitting on a net.</summary>
public sealed record FloorReleaseMessage(string NetId, string ParticipantId);

/// <summary>A floor decision broadcast to interested clients.</summary>
public sealed record FloorEventMessage(string NetId, string Outcome, string Requester, string? Preempted);

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
/// Manager/app -&gt; media service: degrade a specific listener's reception
/// (quality and clarity expressed as 0-100 percentages), optionally scoped to one net.
/// </summary>
public sealed record DegradeCommand(
    string TargetClientId,
    string? NetId,
    int QualityPercent,
    int ClarityPercent);
