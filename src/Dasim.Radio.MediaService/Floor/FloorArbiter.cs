using Dasim.Radio.Contracts;
using Dasim.Radio.Core;
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
    private readonly ILogger<FloorArbiter> _logger;

    public FloorArbiter(
        FloorControlService floor,
        IFloorPriorityResolver priority,
        IFloorSignal signal,
        IFloorStateWriter stateWriter,
        ILogger<FloorArbiter> logger)
    {
        _floor = floor ?? throw new ArgumentNullException(nameof(floor));
        _priority = priority ?? throw new ArgumentNullException(nameof(priority));
        _signal = signal ?? throw new ArgumentNullException(nameof(signal));
        _stateWriter = stateWriter ?? throw new ArgumentNullException(nameof(stateWriter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Arbitrates a push-to-talk request (PTT pressed).</summary>
    public async ValueTask HandleRequestAsync(FloorRequestMessage request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var net = new NetId(request.NetId);
        var participant = new ParticipantId(request.ParticipantId);
        Priority priority = await _priority
            .ResolveAsync(participant, new Priority(request.Priority), cancellationToken)
            .ConfigureAwait(false);

        FloorDecision decision = _floor.RequestFloor(net, participant, priority);

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

        FloorDecision decision = _floor.ReleaseFloor(net, participant);
        if (!decision.IsGranted)
        {
            // A release from someone who is not the holder is a no-op; nothing to broadcast.
            _logger.LogDebug(
                "Ignored floor release on net {Net} from non-holder {Participant}.", net.Value, participant.Value);
            return;
        }

        // Core reuses FloorOutcome.Granted for a successful release; deliberately re-label it as
        // Released so clients can tell "floor freed" apart from "floor acquired".
        await EmitAsync(net, FloorOutcomes.Released, participant, preempted: null, persistState: true, cancellationToken)
            .ConfigureAwait(false);
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
