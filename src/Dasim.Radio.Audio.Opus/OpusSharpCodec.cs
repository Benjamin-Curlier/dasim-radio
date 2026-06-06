using System.Runtime.InteropServices;
using EncoderCtl = global::OpusSharp.Core.EncoderCTL;
using NativeApplication = global::OpusSharp.Core.OpusPredefinedValues;
using NativeOpusDecoder = global::OpusSharp.Core.OpusDecoder;
using NativeOpusEncoder = global::OpusSharp.Core.OpusEncoder;

namespace Dasim.Radio.Audio.Opus;

internal static class OpusSharpInterop
{
    public static NativeApplication ToNative(this OpusApplication application) => application switch
    {
        OpusApplication.Voip => NativeApplication.OPUS_APPLICATION_VOIP,
        OpusApplication.Audio => NativeApplication.OPUS_APPLICATION_AUDIO,
        OpusApplication.RestrictedLowDelay => NativeApplication.OPUS_APPLICATION_RESTRICTED_LOWDELAY,
        _ => throw new ArgumentOutOfRangeException(
            nameof(application), application, "Unknown Opus application."),
    };

    /// <summary>
    /// Reinterprets a <see cref="ReadOnlySpan{T}"/> as a writable <see cref="Span{T}"/> WITHOUT
    /// copying, so it can be handed to OpusSharp's writable-span overloads on the hot path. Valid
    /// ONLY for libopus inputs the native side treats as <c>const</c> — <c>opus_encode</c>'s PCM and
    /// <c>opus_decode</c>'s packet — where the native call never writes through the pointer. Never
    /// use it for an API that could mutate its input.
    /// </summary>
    public static Span<T> AsNativeConstInput<T>(this ReadOnlySpan<T> source) =>
        MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(source), source.Length);
}

/// <summary>Native-libopus (OpusSharp) <see cref="IOpusEncoder"/>. One instance per stream; not thread-safe.</summary>
public sealed class OpusSharpEncoder : IOpusEncoder
{
    private readonly NativeOpusEncoder _encoder;

    internal OpusSharpEncoder(AudioFormat format, OpusEncoderSettings settings)
    {
        Format = format;
        _encoder = new NativeOpusEncoder(
            format.SampleRateHz, format.Channels, settings.Application.ToNative(), use_static: null);

        try
        {
            ApplyTuning(settings);
        }
        catch
        {
            // Release the native handle deterministically if configuration fails mid-construction.
            _encoder.Dispose();
            throw;
        }
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
        return _encoder.Encode(pcm.AsNativeConstInput(), Format.SamplesPerChannel, output, output.Length);
    }

    public void Retune(OpusEncoderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        ApplyTuning(settings);
    }

    public void Dispose()
    {
        _encoder.Dispose();
        GC.SuppressFinalize(this);
    }

    // Sets only the runtime-adjustable CTLs; the application/sample-rate are bound at creation.
    private void ApplyTuning(OpusEncoderSettings settings)
    {
        _encoder.Ctl(EncoderCtl.OPUS_SET_BITRATE, settings.BitrateBitsPerSecond);
        _encoder.Ctl(EncoderCtl.OPUS_SET_COMPLEXITY, settings.Complexity);
        _encoder.Ctl(EncoderCtl.OPUS_SET_DTX, settings.UseDtx ? 1 : 0);
        _encoder.Ctl(EncoderCtl.OPUS_SET_INBAND_FEC, settings.UseInBandFec ? 1 : 0);
        _encoder.Ctl(EncoderCtl.OPUS_SET_PACKET_LOSS_PERC, settings.ExpectedPacketLossPercent);
    }
}

/// <summary>Native-libopus (OpusSharp) <see cref="IOpusDecoder"/>. One instance per stream; not thread-safe.</summary>
public sealed class OpusSharpDecoder : IOpusDecoder
{
    private readonly NativeOpusDecoder _decoder;

    internal OpusSharpDecoder(AudioFormat format)
    {
        Format = format;
        _decoder = new NativeOpusDecoder(format.SampleRateHz, format.Channels, use_static: null);
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
        return _decoder.Decode(opus.AsNativeConstInput(), opus.Length, pcm, Format.SamplesPerChannel, decode_fec: false);
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
        return _decoder.Decode(nextPacket.AsNativeConstInput(), nextPacket.Length, pcm, Format.SamplesPerChannel, decode_fec: true);
    }

    public int DecodeLost(Span<short> pcm)
    {
        EnsurePcmCapacity(pcm);

        // An empty input span pins to a null pointer, which libopus reads as a lost packet and
        // answers with concealment audio.
        return _decoder.Decode(Span<byte>.Empty, 0, pcm, Format.SamplesPerChannel, decode_fec: false);
    }

    public void Dispose()
    {
        _decoder.Dispose();
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

/// <summary>Creates native-libopus encoders. Stateless and thread-safe; register as a singleton.</summary>
public sealed class OpusSharpEncoderFactory : IOpusEncoderFactory
{
    public IOpusEncoder Create(AudioFormat format, OpusEncoderSettings? settings = null)
    {
        settings ??= new OpusEncoderSettings();
        settings.Validate();
        return new OpusSharpEncoder(format, settings);
    }
}

/// <summary>Creates native-libopus decoders. Stateless and thread-safe; register as a singleton.</summary>
public sealed class OpusSharpDecoderFactory : IOpusDecoderFactory
{
    public IOpusDecoder Create(AudioFormat format) => new OpusSharpDecoder(format);
}
