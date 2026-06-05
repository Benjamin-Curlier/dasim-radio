namespace Dasim.Radio.Audio;

/// <summary>Opus coding mode (maps to <c>OPUS_APPLICATION_*</c>).</summary>
public enum OpusApplication
{
    /// <summary>Optimised for speech — the radio default.</summary>
    Voip,

    /// <summary>Optimised for general audio / music.</summary>
    Audio,

    /// <summary>Lowest algorithmic delay; disables the speech-optimised modes.</summary>
    RestrictedLowDelay,
}

/// <summary>
/// Tuning for an Opus encoder. Defaults target LAN voice. Lower <see cref="Complexity"/> (5–7)
/// on the media service, where many encodes run per 20 ms; a client encoding one stream can
/// afford 10.
/// </summary>
public sealed record OpusEncoderSettings
{
    /// <summary>Coding mode. Defaults to <see cref="OpusApplication.Voip"/>.</summary>
    public OpusApplication Application { get; init; } = OpusApplication.Voip;

    /// <summary>Target bitrate in bits per second.</summary>
    public int BitrateBitsPerSecond { get; init; } = 24_000;

    /// <summary>Encoder complexity 0–10 (trades CPU for quality).</summary>
    public int Complexity { get; init; } = 10;

    /// <summary>Discontinuous transmission: emit tiny frames during silence.</summary>
    public bool UseDtx { get; init; }

    /// <summary>In-band forward error correction; pair with <see cref="ExpectedPacketLossPercent"/>.</summary>
    public bool UseInBandFec { get; init; }

    /// <summary>Expected packet loss 0–100, driving FEC redundancy. 0 effectively disables FEC.</summary>
    public int ExpectedPacketLossPercent { get; init; }
}

/// <summary>
/// A stateful Opus encoder bound to one <see cref="AudioFormat"/>. Create one per stream and
/// reuse it across frames — Opus carries inter-frame state. Not thread-safe: confine each
/// instance to a single producer.
/// </summary>
public interface IOpusEncoder : IDisposable
{
    /// <summary>The format this encoder was created for.</summary>
    AudioFormat Format { get; }

    /// <summary>
    /// Encodes exactly one frame of interleaved 16-bit PCM (length must equal
    /// <see cref="AudioFormat.SamplesPerFrame"/>) into <paramref name="output"/>, which must be at
    /// least <see cref="OpusConstants.RecommendedMaxPacketBytes"/> bytes. The call neither
    /// allocates nor retains either buffer — pass pooled spans — and is synchronous and CPU-bound.
    /// Returns the number of bytes written; a very small frame (≤ 2 bytes) is a DTX / comfort-noise
    /// update the sender may choose not to transmit.
    /// </summary>
    int Encode(ReadOnlySpan<short> pcm, Span<byte> output);
}

/// <summary>
/// A stateful Opus decoder bound to one <see cref="AudioFormat"/>. Create one per stream and
/// reuse it across frames. Not thread-safe: confine each instance to a single consumer.
/// </summary>
public interface IOpusDecoder : IDisposable
{
    /// <summary>The format this decoder produces.</summary>
    AudioFormat Format { get; }

    /// <summary>
    /// Decodes one received Opus packet into interleaved 16-bit PCM (<paramref name="pcm"/> length
    /// must be at least <see cref="AudioFormat.SamplesPerFrame"/>). Returns samples-per-channel
    /// decoded. Throws if <paramref name="opus"/> is empty — route a dropped packet through
    /// <see cref="DecodeLost"/> or <see cref="DecodeFec"/> instead.
    /// </summary>
    int Decode(ReadOnlySpan<byte> opus, Span<short> pcm);

    /// <summary>
    /// Recovers a single lost frame from the in-band forward error correction carried by the
    /// <em>following</em> packet. When packet N is lost and packet N+1 arrives, pass N+1 here to
    /// reconstruct N, then call <see cref="Decode"/> on N+1 to obtain N+1 itself. Requires the
    /// sender to have enabled <see cref="OpusEncoderSettings.UseInBandFec"/>; falls back to
    /// concealment when the packet carries no FEC data. Returns samples-per-channel produced.
    /// </summary>
    int DecodeFec(ReadOnlySpan<byte> nextPacket, Span<short> pcm);

    /// <summary>
    /// Produces packet-loss concealment audio when a packet is lost and no FEC is available. Fills
    /// <paramref name="pcm"/> (length ≥ <see cref="AudioFormat.SamplesPerFrame"/>) and returns
    /// samples-per-channel produced.
    /// </summary>
    int DecodeLost(Span<short> pcm);
}

/// <summary>Creates <see cref="IOpusEncoder"/> instances. Implementations are thread-safe and reusable.</summary>
public interface IOpusEncoderFactory
{
    /// <summary>Creates an encoder for <paramref name="format"/> with the given (or default) settings.</summary>
    IOpusEncoder Create(AudioFormat format, OpusEncoderSettings? settings = null);
}

/// <summary>Creates <see cref="IOpusDecoder"/> instances. Implementations are thread-safe and reusable.</summary>
public interface IOpusDecoderFactory
{
    /// <summary>Creates a decoder for <paramref name="format"/>.</summary>
    IOpusDecoder Create(AudioFormat format);
}
