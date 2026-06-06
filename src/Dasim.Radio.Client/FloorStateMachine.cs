using Dasim.Radio.Contracts;

namespace Dasim.Radio.Client;

/// <summary>How an incoming <see cref="FloorEventMessage"/> on our transmit net affects us.</summary>
public enum FloorInput
{
    /// <summary>The net's holder is now us — we have the floor.</summary>
    GrantedToUs,

    /// <summary>Our request was explicitly denied.</summary>
    DeniedToUs,

    /// <summary>Someone else holds the net (we didn't get it, or we were pre-empted out of it).</summary>
    LostFloor,

    /// <summary>The net went idle (the holder released).</summary>
    NetIdle,
}

/// <summary>The side effect the engine must perform after a transition.</summary>
public enum FloorEffect
{
    /// <summary>Do nothing.</summary>
    None,

    /// <summary>Publish a <c>floor.request</c> for our transmit net.</summary>
    SendRequest,

    /// <summary>Publish a <c>floor.release</c> for our transmit net.</summary>
    SendRelease,
}

/// <summary>
/// The client-side push-to-talk state machine — the local mirror of the authoritative
/// <c>FloorControlService</c>. It is pure (no I/O): the engine feeds it PTT and floor inputs and
/// performs the returned <see cref="FloorEffect"/>. The client NEVER assumes it holds the floor just
/// because PTT is down — it transmits only after the media service grants it
/// (<see cref="PttPhase.Transmitting"/>), and learns of a pre-emption from the broadcast event.
/// </summary>
public sealed class FloorStateMachine
{
    /// <summary>The current phase.</summary>
    public PttPhase Phase { get; private set; } = PttPhase.Idle;

    /// <summary>True only while we actually hold the floor.</summary>
    public bool IsTransmitting => Phase == PttPhase.Transmitting;

    /// <summary>PTT pressed: request the floor if we were idle.</summary>
    public FloorEffect OnPttPressed()
    {
        if (Phase == PttPhase.Idle)
        {
            Phase = PttPhase.Requesting;
            return FloorEffect.SendRequest;
        }

        return FloorEffect.None;
    }

    /// <summary>PTT released: drop any held/pending floor.</summary>
    public FloorEffect OnPttReleased()
    {
        switch (Phase)
        {
            // Requesting → cancel the still-pending request; Transmitting → release the held floor.
            case PttPhase.Requesting:
            case PttPhase.Transmitting:
                Phase = PttPhase.Idle;
                return FloorEffect.SendRelease;

            // Denied/Preempted: our request already resolved and we hold nothing, so there is nothing
            // to release — just go idle without adding control-plane chatter.
            case PttPhase.Denied:
            case PttPhase.Preempted:
                Phase = PttPhase.Idle;
                return FloorEffect.None;

            default:
                return FloorEffect.None;
        }
    }

    /// <summary>A floor decision arrived for our transmit net.</summary>
    public FloorEffect OnFloor(FloorInput input)
    {
        switch (input)
        {
            // The media service is authoritative: if it says we hold the net, we transmit — even from
            // Denied/Preempted (a queued grant can arrive without an intervening idle event). Only an
            // Idle phase ignores a grant, since PTT is up and the grant is stale.
            case FloorInput.GrantedToUs when Phase != PttPhase.Idle:
                Phase = PttPhase.Transmitting;
                return FloorEffect.None;

            case FloorInput.DeniedToUs when Phase == PttPhase.Requesting:
                Phase = PttPhase.Denied;
                return FloorEffect.None;

            case FloorInput.LostFloor when Phase == PttPhase.Requesting:
                Phase = PttPhase.Denied;
                return FloorEffect.None;

            case FloorInput.LostFloor when Phase == PttPhase.Transmitting:
                Phase = PttPhase.Preempted;
                return FloorEffect.None;

            // We're still holding PTT down and the net just freed up — re-request it.
            case FloorInput.NetIdle when Phase is PttPhase.Denied or PttPhase.Preempted:
                Phase = PttPhase.Requesting;
                return FloorEffect.SendRequest;

            default:
                return FloorEffect.None;
        }
    }
}

/// <summary>Translates a broadcast <see cref="FloorEventMessage"/> into a <see cref="FloorInput"/> for us.</summary>
public static class FloorEventInterpreter
{
    /// <summary>Interprets <paramref name="event"/> from the point of view of <paramref name="participantId"/>.</summary>
    public static FloorInput Interpret(FloorEventMessage @event, string participantId)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (string.Equals(@event.CurrentHolder, participantId, StringComparison.Ordinal))
        {
            return FloorInput.GrantedToUs;
        }

        if (string.Equals(@event.Outcome, FloorOutcomes.Denied, StringComparison.Ordinal)
            && string.Equals(@event.Requester, participantId, StringComparison.Ordinal))
        {
            return FloorInput.DeniedToUs;
        }

        return @event.CurrentHolder is null ? FloorInput.NetIdle : FloorInput.LostFloor;
    }
}
