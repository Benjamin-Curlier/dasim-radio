// The third-party package's root namespace is `Concentus`, which clashes with this project's
// own `Dasim.Radio.Audio.Concentus` namespace. Bind the library's types through `global::`
// aliases so references are unambiguous regardless of the enclosing namespace.
using ConcentusApplication = global::Concentus.Enums.OpusApplication;
using NativeFactory = global::Concentus.OpusCodecFactory;
using NativeOpusDecoder = global::Concentus.IOpusDecoder;
using NativeOpusEncoder = global::Concentus.IOpusEncoder;

namespace Dasim.Radio.Audio.Concentus;

internal static class ConcentusInterop
{
    public static ConcentusApplication ToConcentus(this OpusApplication application) => application switch
    {
        OpusApplication.Voip => ConcentusApplication.OPUS_APPLICATION_VOIP,
        OpusApplication.Audio => ConcentusApplication.OPUS_APPLICATION_AUDIO,
        OpusApplication.RestrictedLowDelay => ConcentusApplication.OPUS_APPLICATION_RESTRICTED_LOWDELAY,
        _ => throw new ArgumentOutOfRangeException(
            nameof(application), application, "Unknown Opus application."),
    };
}

/// <summary>Managed-Opus (Concentus) <see cref="IOpusEncoder"/>. One instance per stream; not thread-safe.</summary>
public sealed class ConcentusOpusEncoder : IOpusEncoder
{
    private readonly NativeOpusEncoder _encoder;

    internal ConcentusOpusEncoder(AudioFormat format, OpusEncoderSettings settings)
    {
        Format = format;
        _encoder = NativeFactory.CreateEncoder(
                format.SampleRateHz, format.Channels, settings.Application.ToConcentus(), null)
            ?? throw new InvalidOperationException("Concentus returned a null encoder.");

        ApplyTuning(settings);
    }

    public AudioFormat Format { get; }

    public int Encode(ReadOnlySpan<short> pcm, Span<byte> output)
    {
        if (pcm.Length != Format.SamplesPerFrame)
        {
            throw new ArgumentException(
                $"Expected exactly {Format.SamplesPerFrame} interleaved samples " +
                $"({Format.SamplesPerChannel}/channel × {Format.Channels}ch), got {pcm.Length}.",
                nameof(pcm));
        }

        // Opus frame_size is samples *per channel*; max_data_bytes bounds the packet.
        return _encoder.Encode(pcm, Format.SamplesPerChannel, output, output.Length);
    }

    public void Retune(OpusEncoderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        ApplyTuning(settings);
    }

    public void Dispose()
    {
        (_encoder as IDisposable)?.Dispose();
        GC.SuppressFinalize(this);
    }

    // Sets only the runtime-adjustable parameters; the application/sample-rate are bound at creation.
    private void ApplyTuning(OpusEncoderSettings settings)
    {
        _encoder.Bitrate = settings.BitrateBitsPerSecond;
        _encoder.Complexity = settings.Complexity;
        _encoder.UseDTX = settings.UseDtx;
        _encoder.UseInbandFEC = settings.UseInBandFec;
        _encoder.PacketLossPercent = settings.ExpectedPacketLossPercent;
    }
}

/// <summary>Managed-Opus (Concentus) <see cref="IOpusDecoder"/>. One instance per stream; not thread-safe.</summary>
public sealed class ConcentusOpusDecoder : IOpusDecoder
{
    private readonly NativeOpusDecoder _decoder;

    internal ConcentusOpusDecoder(AudioFormat format)
    {
        Format = format;
        _decoder = NativeFactory.CreateDecoder(format.SampleRateHz, format.Channels, null)
            ?? throw new InvalidOperationException("Concentus returned a null decoder.");
    }

    public AudioFormat Format { get; }

    public int Decode(ReadOnlySpan<byte> opus, Span<short> pcm)
    {
        if (opus.IsEmpty)
        {
            throw new ArgumentException(
                "Opus packet must not be empty; route a dropped packet through DecodeLost or DecodeFec.",
                nameof(opus));
        }

        EnsurePcmCapacity(pcm);
        return _decoder.Decode(opus, pcm, Format.SamplesPerChannel, decode_fec: false);
    }

    public int DecodeFec(ReadOnlySpan<byte> nextPacket, Span<short> pcm)
    {
        if (nextPacket.IsEmpty)
        {
            throw new ArgumentException(
                "The following packet must not be empty; use DecodeLost when no FEC is available.",
                nameof(nextPacket));
        }

        EnsurePcmCapacity(pcm);
        return _decoder.Decode(nextPacket, pcm, Format.SamplesPerChannel, decode_fec: true);
    }

    public int DecodeLost(Span<short> pcm)
    {
        EnsurePcmCapacity(pcm);

        // An empty packet with FEC off drives Opus packet-loss concealment.
        return _decoder.Decode(ReadOnlySpan<byte>.Empty, pcm, Format.SamplesPerChannel, decode_fec: false);
    }

    public void Dispose()
    {
        (_decoder as IDisposable)?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void EnsurePcmCapacity(Span<short> pcm)
    {
        if (pcm.Length < Format.SamplesPerFrame)
        {
            throw new ArgumentException(
                $"PCM buffer must hold at least {Format.SamplesPerFrame} interleaved samples, got {pcm.Length}.",
                nameof(pcm));
        }
    }
}

/// <summary>Creates managed-Opus encoders. Stateless and thread-safe; register as a singleton.</summary>
public sealed class ConcentusOpusEncoderFactory : IOpusEncoderFactory
{
    public IOpusEncoder Create(AudioFormat format, OpusEncoderSettings? settings = null)
    {
        settings ??= new OpusEncoderSettings();
        settings.Validate();
        return new ConcentusOpusEncoder(format, settings);
    }
}

/// <summary>Creates managed-Opus decoders. Stateless and thread-safe; register as a singleton.</summary>
public sealed class ConcentusOpusDecoderFactory : IOpusDecoderFactory
{
    public IOpusDecoder Create(AudioFormat format) => new ConcentusOpusDecoder(format);
}
