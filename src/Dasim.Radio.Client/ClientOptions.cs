using Dasim.Radio.Audio;

namespace Dasim.Radio.Client;

/// <summary>
/// Configuration for one radio client, bound from the <c>Client</c> configuration section.
/// </summary>
public sealed class ClientOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Client";

    /// <summary>
    /// This client's audio identity — the token in <c>audio.in.&lt;clientId&gt;</c> /
    /// <c>audio.out.&lt;clientId&gt;</c>. Must be a single NATS token (no <c>.</c>, <c>*</c>, <c>&gt;</c>
    /// or whitespace).
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// This client's floor identity — the force-tree node id sent in floor requests. The media
    /// service's <c>ForceTreePriorityResolver</c> derives the authoritative priority from this, so it
    /// must match the participant's node in the force tree. Often the same value as <see cref="ClientId"/>.
    /// </summary>
    public string ParticipantId { get; set; } = string.Empty;

    /// <summary>The net this client transmits on (and listens to). For a leader this is the net it owns.</summary>
    public string OwnNetId { get; set; } = string.Empty;

    /// <summary>
    /// The parent net this client also listens to (talk-up), or <c>null</c> for a leaf. Transmitting
    /// up to the parent net is a follow-up; for now this net is listen-only for floor-holder display.
    /// </summary>
    public string? ParentNetId { get; set; }

    /// <summary>
    /// Advisory priority placed in <c>floor.request</c>. The media service overrides it with the
    /// force-tree-derived value, so this is informational only; defaults to 0.
    /// </summary>
    public int AdvertisedPriority { get; set; }

    /// <summary>Opus encoder tuning for the transmit stream. Defaults target LAN voice.</summary>
    public OpusEncoderSettings EncoderSettings { get; set; } = new();

    /// <summary>
    /// How long <c>StartAsync</c> waits for the floor-event subscriptions to register before enabling PTT
    /// input anyway. A LAN SUB round-trip is sub-millisecond; this only bounds a slow/absent broker.
    /// </summary>
    public TimeSpan FloorSubscribeReadyTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
