namespace Dasim.Radio.Messaging.Audio;

/// <summary>One Opus frame on the audio data plane, tagged with the client that produced it.</summary>
public readonly record struct AudioFrame(string ClientId, byte[] Opus);

/// <summary>
/// The audio data plane over <b>core NATS</b> (never JetStream). Clients publish captured Opus
/// frames and subscribe to their own per-listener mix; the media service subscribes to every
/// client's captured audio and publishes the mixes.
/// </summary>
/// <remarks>
/// Subscriptions surface one <c>byte[]</c> per frame (the deserialized NATS payload). At
/// ≤50 participants × 50 fps this allocation is negligible; the media service's per-listener
/// encode fan-out — not this transport seam — is where pooled buffers matter.
/// </remarks>
public interface IAudioBus
{
    /// <summary>Publishes a captured Opus frame to <c>audio.in.&lt;clientId&gt;</c>.</summary>
    ValueTask PublishCapturedAsync(string clientId, ReadOnlyMemory<byte> opusFrame, CancellationToken cancellationToken = default);

    /// <summary>Publishes a per-listener mixed Opus frame to <c>audio.out.&lt;listenerClientId&gt;</c>.</summary>
    ValueTask PublishMixedAsync(string listenerClientId, ReadOnlyMemory<byte> opusFrame, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to every client's captured audio (<c>audio.in.&gt;</c>) — media service side.</summary>
    IAsyncEnumerable<AudioFrame> SubscribeCapturedAsync(CancellationToken cancellationToken = default);

    /// <summary>Subscribes to one listener's mix (<c>audio.out.&lt;clientId&gt;</c>) — client side.</summary>
    IAsyncEnumerable<byte[]> SubscribeMixedAsync(string clientId, CancellationToken cancellationToken = default);
}
