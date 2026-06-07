using System.Collections.Concurrent;

namespace Dasim.Radio.Core;

/// <summary>State of a net's floor.</summary>
public enum FloorStatus
{
    Idle,
    Held,
}

/// <summary>Result of a floor request or release.</summary>
public enum FloorOutcome
{
    Granted,
    GrantedWithPreemption,
    Denied,
}

/// <summary>Immutable view of a net's floor at a point in time.</summary>
public sealed record FloorSnapshot(
    NetId Net,
    FloorStatus Status,
    ParticipantId? Holder,
    Priority? HolderPriority,
    DateTimeOffset? HeldSince);

/// <summary>Outcome of a floor operation, including who (if anyone) was pre-empted.</summary>
public sealed record FloorDecision(
    FloorOutcome Outcome,
    NetId Net,
    ParticipantId Requester,
    ParticipantId? PreemptedParticipant = null,
    string? Reason = null)
{
    public bool IsGranted => Outcome is FloorOutcome.Granted or FloorOutcome.GrantedWithPreemption;

    /// <summary>
    /// Whether this decision actually changed the held set. True for every grant, pre-emption,
    /// priority refresh and release; false only for a no-op idempotent re-grant (the holder re-requested
    /// at the same priority). Drives <see cref="FloorControlService.Version"/> so a chatty keep-alive
    /// does not force the router to rebuild its holder snapshot for nothing.
    /// </summary>
    public bool Changed { get; init; } = true;
}

/// <summary>
/// Authoritative push-to-talk floor arbitration, one floor per net. Strict pre-emption:
/// a strictly higher-priority request cuts off the current holder; an equal or lower priority
/// request is denied while the floor is held. Thread-safe; intended to be owned by the central
/// media service so every client observes the same decisions.
/// </summary>
public sealed class FloorControlService
{
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<NetId, NetFloor> _nets = new();
    private long _version;

    public FloorControlService(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <summary>
    /// A monotonic counter bumped whenever the held set changes (a grant, pre-emption, priority
    /// refresh or release). Lets a per-frame reader — the router's <c>FloorControlHolders</c> — cache
    /// its holder snapshot and rebuild it only when this changes, instead of every 20 ms tick. A
    /// denied request leaves it untouched.
    /// </summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>
    /// Requests transmission on a net (push-to-talk pressed). <paramref name="sequence"/> is the
    /// requester's per-participant monotonic press counter; it lets a later <see cref="ReleaseFloor"/>
    /// be matched to the press it releases so a reordered stale release can be rejected. 0 = unsequenced.
    /// </summary>
    public FloorDecision RequestFloor(NetId net, ParticipantId participant, Priority priority, long sequence = 0)
    {
        FloorDecision decision = GetFloor(net).Request(participant, priority, sequence, _clock.GetUtcNow());
        if (decision.IsGranted && decision.Changed)
        {
            Interlocked.Increment(ref _version);
        }

        return decision;
    }

    /// <summary>
    /// Releases transmission on a net (push-to-talk released). A release whose <paramref name="sequence"/>
    /// is older than the press the holder currently holds is rejected (a transport reorder placed it after
    /// the holder re-acquired the floor) so it cannot free a legitimately held floor.
    /// </summary>
    public FloorDecision ReleaseFloor(NetId net, ParticipantId participant, long sequence = 0)
    {
        FloorDecision decision = GetFloor(net).Release(participant, sequence);
        if (decision.IsGranted && decision.Changed)
        {
            Interlocked.Increment(ref _version);
        }

        return decision;
    }

    /// <summary>Returns the current state of a net's floor.</summary>
    public FloorSnapshot GetSnapshot(NetId net) => GetFloor(net).Snapshot();

    /// <summary>
    /// Snapshots every net that currently holds a floor (idle nets are omitted). The media service's
    /// router uses this to learn which speakers are live this instant without round-tripping NATS.
    /// </summary>
    public IReadOnlyList<FloorSnapshot> ActiveFloors()
    {
        var held = new List<FloorSnapshot>();
        foreach (NetFloor floor in _nets.Values)
        {
            FloorSnapshot snapshot = floor.Snapshot();
            if (snapshot.Status == FloorStatus.Held)
            {
                held.Add(snapshot);
            }
        }

        return held;
    }

    private NetFloor GetFloor(NetId net) => _nets.GetOrAdd(net, static n => new NetFloor(n));

    private sealed class NetFloor(NetId net)
    {
        private readonly Lock _gate = new();
        private ParticipantId? _holder;
        private Priority? _priority;
        private DateTimeOffset? _heldSince;
        private long _sequence;

        public FloorDecision Request(ParticipantId participant, Priority priority, long sequence, DateTimeOffset now)
        {
            lock (_gate)
            {
                if (_holder is null)
                {
                    Acquire(participant, priority, sequence, now);
                    return new FloorDecision(FloorOutcome.Granted, net, participant);
                }

                if (_holder.Value == participant)
                {
                    // Already holding: refresh priority and grant idempotently. Only a priority change
                    // actually mutates the held set (the holder's priority is part of what the router
                    // reads), so a same-priority re-request is a no-op for versioning purposes. Always
                    // advance to the latest press's sequence so an older, reordered release is rejected.
                    bool changed = _priority!.Value != priority;
                    _priority = priority;
                    _sequence = sequence;
                    return new FloorDecision(FloorOutcome.Granted, net, participant) { Changed = changed };
                }

                if (priority > _priority!.Value)
                {
                    ParticipantId preempted = _holder.Value;
                    Acquire(participant, priority, sequence, now);
                    return new FloorDecision(
                        FloorOutcome.GrantedWithPreemption, net, participant, preempted);
                }

                return new FloorDecision(
                    FloorOutcome.Denied,
                    net,
                    participant,
                    Reason: $"Floor held by '{_holder.Value}' at equal or higher priority.");
            }
        }

        public FloorDecision Release(ParticipantId participant, long sequence)
        {
            lock (_gate)
            {
                if (_holder is null || _holder.Value != participant)
                {
                    return new FloorDecision(
                        FloorOutcome.Denied, net, participant, Reason: "Not the current floor holder.");
                }

                if (sequence != _sequence)
                {
                    // The release must match the exact press currently held. Older (< _sequence) = a stale
                    // release a transport reorder placed after the holder re-acquired the floor; newer = a
                    // release that overtook its own request (only reachable if a future client breaks the
                    // request-before-release ordering). Either way, don't free a live hold — that would drop
                    // the operator's in-progress transmission and emit a spurious 'released' broadcast.
                    // Restart-reset is safe: the holder's re-assert overwrote _sequence to the press it now
                    // holds, so the matching release still equals it.
                    return new FloorDecision(
                        FloorOutcome.Denied, net, participant,
                        Reason: "Release does not match the currently held press.");
                }

                _holder = null;
                _priority = null;
                _heldSince = null;
                _sequence = 0;
                return new FloorDecision(FloorOutcome.Granted, net, participant);
            }
        }

        public FloorSnapshot Snapshot()
        {
            lock (_gate)
            {
                return _holder is null
                    ? new FloorSnapshot(net, FloorStatus.Idle, null, null, null)
                    : new FloorSnapshot(net, FloorStatus.Held, _holder, _priority, _heldSince);
            }
        }

        private void Acquire(ParticipantId participant, Priority priority, long sequence, DateTimeOffset now)
        {
            _holder = participant;
            _priority = priority;
            _heldSince = now;
            _sequence = sequence;
        }
    }
}
