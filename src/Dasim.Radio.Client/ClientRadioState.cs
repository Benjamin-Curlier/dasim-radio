namespace Dasim.Radio.Client;

/// <summary>The client's push-to-talk phase, mirrored from the authoritative media-service floor.</summary>
public enum PttPhase
{
    /// <summary>PTT is up; this client holds no floor.</summary>
    Idle,

    /// <summary>PTT is down; a floor request was sent and we're awaiting the media service's decision.</summary>
    Requesting,

    /// <summary>We hold the floor and are transmitting.</summary>
    Transmitting,

    /// <summary>PTT is down but our request was denied (the net is held at equal or higher priority).</summary>
    Denied,

    /// <summary>PTT is still down but a higher priority pre-empted us mid-transmission.</summary>
    Preempted,
}

/// <summary>
/// An immutable snapshot of the client's radio state for the UI. The engine publishes a new snapshot
/// (and raises its state-changed event) on every transition — view models bind to this rather than to
/// the engine's internals.
/// </summary>
public sealed record ClientRadioState
{
    /// <summary>The current push-to-talk phase.</summary>
    public PttPhase Phase { get; init; } = PttPhase.Idle;

    /// <summary>Whether captured audio is currently being transmitted (true only in <see cref="PttPhase.Transmitting"/>).</summary>
    public bool IsTransmitting => Phase == PttPhase.Transmitting;

    /// <summary>The net this client transmits on.</summary>
    public string TransmitNetId { get; init; } = string.Empty;

    /// <summary>
    /// Current floor holder per listened net (participant id, or <c>null</c> when the net is idle).
    /// Lets the UI show who is talking on each net.
    /// </summary>
    public IReadOnlyDictionary<string, string?> FloorHolders { get; init; } =
        new Dictionary<string, string?>();
}
