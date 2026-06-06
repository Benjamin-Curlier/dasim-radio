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

    public FloorControlService(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <summary>Requests transmission on a net (push-to-talk pressed).</summary>
    public FloorDecision RequestFloor(NetId net, ParticipantId participant, Priority priority) =>
        GetFloor(net).Request(participant, priority, _clock.GetUtcNow());

    /// <summary>Releases transmission on a net (push-to-talk released).</summary>
    public FloorDecision ReleaseFloor(NetId net, ParticipantId participant) =>
        GetFloor(net).Release(participant);

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

        public FloorDecision Request(ParticipantId participant, Priority priority, DateTimeOffset now)
        {
            lock (_gate)
            {
                if (_holder is null)
                {
                    Acquire(participant, priority, now);
                    return new FloorDecision(FloorOutcome.Granted, net, participant);
                }

                if (_holder.Value == participant)
                {
                    // Already holding: refresh priority and grant idempotently.
                    _priority = priority;
                    return new FloorDecision(FloorOutcome.Granted, net, participant);
                }

                if (priority > _priority!.Value)
                {
                    ParticipantId preempted = _holder.Value;
                    Acquire(participant, priority, now);
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

        public FloorDecision Release(ParticipantId participant)
        {
            lock (_gate)
            {
                if (_holder is not null && _holder.Value == participant)
                {
                    _holder = null;
                    _priority = null;
                    _heldSince = null;
                    return new FloorDecision(FloorOutcome.Granted, net, participant);
                }

                return new FloorDecision(
                    FloorOutcome.Denied, net, participant, Reason: "Not the current floor holder.");
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

        private void Acquire(ParticipantId participant, Priority priority, DateTimeOffset now)
        {
            _holder = participant;
            _priority = priority;
            _heldSince = now;
        }
    }
}
