using Dasim.Radio.Contracts;
using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Routing;
using Dasim.Radio.Messaging.Floor;
using Microsoft.Extensions.Logging;

namespace Dasim.Radio.MediaService.Floor;

/// <summary>
/// The media service's floor authority: turns a wire push-to-talk request/release into an
/// authoritative <see cref="FloorControlService"/> decision, broadcasts the resulting
/// <see cref="FloorEventMessage"/>, and records the net's floor state. Thread-safe (the decision is
/// serialized per net inside <see cref="FloorControlService"/>); this is the unit-tested core, while
/// <see cref="FloorAuthorityService"/> is the thin NATS-pumping host around it.
/// </summary>
public sealed class FloorArbiter
{
    private readonly FloorControlService _floor;
    private readonly IFloorPriorityResolver _priority;
    private readonly IFloorSignal _signal;
    private readonly IFloorStateWriter _stateWriter;
    private readonly IForceTreeProvider _force;
    private readonly ILogger<FloorArbiter> _logger;

    public FloorArbiter(
        FloorControlService floor,
        IFloorPriorityResolver priority,
        IFloorSignal signal,
        IFloorStateWriter stateWriter,
        IForceTreeProvider force,
        ILogger<FloorArbiter> logger)
    {
        _floor = floor ?? throw new ArgumentNullException(nameof(floor));
        _priority = priority ?? throw new ArgumentNullException(nameof(priority));
        _signal = signal ?? throw new ArgumentNullException(nameof(signal));
        _stateWriter = stateWriter ?? throw new ArgumentNullException(nameof(stateWriter));
        _force = force ?? throw new ArgumentNullException(nameof(force));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Arbitrates a push-to-talk request (PTT pressed).</summary>
    public async ValueTask HandleRequestAsync(FloorRequestMessage request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var net = new NetId(request.NetId);
        var participant = new ParticipantId(request.ParticipantId);

        if (!IsMemberOf(participant, net))
        {
            // Authorization + input validation in one gate: a participant may seize the floor only on a
            // net it belongs to in the authoritative force tree. This rejects an unknown participant, a
            // request for a net the participant is not on (floor-hijack / net-silencing), and any forged or
            // malformed NetId — so a wire token never reaches a grant, a floor.events.<netId> publish, a
            // floor_state KV key, or the per-net floor map. Drop silently (no broadcast that could echo to
            // a real net or act as an oracle); log for observability.
            _logger.LogWarning(
                "Rejected floor request from {Participant} for net {Net}: not a member in the force tree.",
                request.ParticipantId, request.NetId);
            return;
        }

        Priority priority = await _priority
            .ResolveAsync(participant, new Priority(request.Priority), cancellationToken)
            .ConfigureAwait(false);

        FloorDecision decision = _floor.RequestFloor(net, participant, priority, request.Sequence);

        string outcome = decision.Outcome switch
        {
            FloorOutcome.Granted => FloorOutcomes.Granted,
            FloorOutcome.GrantedWithPreemption => FloorOutcomes.GrantedWithPreemption,
            FloorOutcome.Denied => FloorOutcomes.Denied,
            _ => throw new ArgumentOutOfRangeException(nameof(request), decision.Outcome, "Unknown floor outcome."),
        };

        await EmitAsync(
            net, outcome, participant, decision.PreemptedParticipant, persistState: decision.IsGranted, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Arbitrates a push-to-talk release (PTT released).</summary>
    public async ValueTask HandleReleaseAsync(FloorReleaseMessage release, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        var net = new NetId(release.NetId);
        var participant = new ParticipantId(release.ParticipantId);

        if (!IsMemberOf(participant, net))
        {
            // Same gate as the request path — also so a release for a junk net never auto-creates a
            // per-net floor entry (the unbounded-net-map DoS).
            _logger.LogWarning(
                "Rejected floor release from {Participant} for net {Net}: not a member in the force tree.",
                release.ParticipantId, release.NetId);
            return;
        }

        FloorDecision decision = _floor.ReleaseFloor(net, participant, release.Sequence);
        if (!decision.IsGranted)
        {
            // A release from a non-holder, or a stale release superseded by the holder's newer press, is a
            // no-op; nothing to broadcast.
            _logger.LogDebug(
                "Ignored floor release on net {Net} from {Participant}: {Reason}",
                net.Value, participant.Value, decision.Reason);
            return;
        }

        // Core reuses FloorOutcome.Granted for a successful release; deliberately re-label it as
        // Released so clients can tell "floor freed" apart from "floor acquired".
        await EmitAsync(net, FloorOutcomes.Released, participant, preempted: null, persistState: true, cancellationToken)
            .ConfigureAwait(false);
    }

    // A participant may act on a net only if the authoritative force tree lists it as a member — the same
    // membership the router enforces for audio. Force-tree net ids are validated single tokens, so this
    // gate also bars any malformed or forged NetId from reaching a NATS subject, a KV key, or the net map.
    private bool IsMemberOf(ParticipantId participant, NetId net)
    {
        IReadOnlyList<NetId> nets = _force.Current.Topology.MembershipOf(participant).Nets;
        for (int i = 0; i < nets.Count; i++)
        {
            if (nets[i] == net)
            {
                return true;
            }
        }

        return false;
    }

    private async ValueTask EmitAsync(
        NetId net,
        string outcome,
        ParticipantId requester,
        ParticipantId? preempted,
        bool persistState,
        CancellationToken cancellationToken)
    {
        // Reading the post-decision snapshot here is safe because FloorAuthorityService funnels every
        // floor signal through a single consumer — nothing mutates this net between the decision above
        // and this line, so the snapshot (and the CurrentHolder/state derived from it) matches it.
        FloorSnapshot snapshot = _floor.GetSnapshot(net);

        var @event = new FloorEventMessage(
            net.Value, outcome, requester.Value, preempted?.Value, snapshot.Holder?.Value);

        // Publish first: the event is the low-latency authoritative signal clients act on, so a
        // publish failure is a real error and propagates. The KV write below is observability only.
        await _signal.PublishEventAsync(@event, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Floor {Outcome} on net {Net} for {Requester}{Preempted}.",
            outcome,
            net.Value,
            requester.Value,
            preempted is null ? string.Empty : $" (pre-empted {preempted.Value})");

        if (!persistState)
        {
            return;
        }

        var state = new FloorStateDto(
            snapshot.Net.Value, snapshot.Holder?.Value, snapshot.HolderPriority?.Value, snapshot.HeldSince);

        try
        {
            await _stateWriter.WriteAsync(state, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The event already carried the authoritative transition; floor_state is eventually
            // consistent and reconciles on this net's next transition.
            _logger.LogWarning(
                ex, "Failed to persist floor_state for net {Net}; it will reconcile on the next transition.", net.Value);
        }
    }
}
