namespace Dasim.Radio.Contracts;

// State persisted in the JetStream KV buckets (see Subjects.Buckets). Like the wire
// messages, these use primitive types only so they serialize cleanly and stay decoupled
// from the domain model in Dasim.Radio.Core.

/// <summary>
/// A node of the military hierarchy as stored in the <c>force_tree</c> bucket. Mirrors the
/// domain <c>ForceNode</c> with primitives: <see cref="Kind"/> is the echelon name and
/// <see cref="Priority"/> the transmission authority (higher wins the floor).
/// </summary>
public sealed record ForceNodeDto(
    string Id,
    string Name,
    string Kind,
    int Priority,
    ForceNodeDto[] Children);

/// <summary>The whole force tree as stored in the <c>force_tree</c> bucket, with a version.</summary>
public sealed record ForceTreeDto(int Version, ForceNodeDto Root);

/// <summary>A post-to-host association as stored in the <c>endpoints</c> bucket.</summary>
public sealed record EndpointDto(string PostId, string HostName, string IpAddress);

/// <summary>
/// A client launch configuration as stored in the <c>configs</c> bucket (keyed by <see cref="ConfigId"/>).
/// The manager authors these; an agent's <c>launch(configId)</c> resolves one and the client boots from it.
/// Deliberately omits priority (the force tree is authoritative — see <c>ForceTreePriorityResolver</c>) and
/// device/codec preferences (those are local, per-machine concerns, and Contracts must not reference Audio).
/// </summary>
public sealed record ClientConfigDto(
    string ConfigId,
    string ClientId,
    string ParticipantId,
    string OwnNetId,
    string? ParentNetId,
    string DisplayName,
    string? HostId = null);

/// <summary>
/// Authoritative floor holder for one net, as stored in the <c>floor_state</c> bucket.
/// A null <see cref="HolderParticipantId"/> means the net is idle.
/// </summary>
public sealed record FloorStateDto(
    string NetId,
    string? HolderParticipantId,
    int? HolderPriority,
    DateTimeOffset? HeldSinceUtc);
