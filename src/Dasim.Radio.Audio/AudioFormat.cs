namespace Dasim.Radio.Audio;

// The codec seam is deliberately dependency-free: both the Concentus (client) and the
// libopus/OpusSharp (media service) implementations bind to these abstractions, so neither
// codec leaks across the seam. PCM is always interleaved signed 16-bit.

/// <summary>
/// An Opus-legal PCM frame format: sample rate, channel count and frame duration. The canonical
/// radio format is <see cref="Voice"/> (48 kHz, mono, 20 ms = 960 samples per channel).
/// </summary>
public readonly record struct AudioFormat
{
    private static readonly int[] AllowedSampleRates = [8_000, 12_000, 16_000, 24_000, 48_000];
    private static readonly double[] AllowedFrameMilliseconds = [2.5, 5, 10, 20, 40, 60];

    public AudioFormat(int sampleRateHz, int channels, double frameMilliseconds)
    {
        if (Array.IndexOf(AllowedSampleRates, sampleRateHz) < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRateHz), sampleRateHz, "Opus supports 8, 12, 16, 24 or 48 kHz.");
        }

        if (channels is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(
                nameof(channels), channels, "Opus supports 1 (mono) or 2 (stereo) channels.");
        }

        if (Array.IndexOf(AllowedFrameMilliseconds, frameMilliseconds) < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameMilliseconds), frameMilliseconds,
                "Opus frame duration must be 2.5, 5, 10, 20, 40 or 60 ms.");
        }

        SampleRateHz = sampleRateHz;
        Channels = channels;
        FrameMilliseconds = frameMilliseconds;
    }

    public int SampleRateHz { get; }

    public int Channels { get; }

    public double FrameMilliseconds { get; }

    /// <summary>Samples per channel in one frame (the Opus <c>frame_size</c>), e.g. 960 at 48 kHz / 20 ms.</summary>
    public int SamplesPerChannel => (int)(SampleRateHz * FrameMilliseconds / 1000.0);

    /// <summary>Total interleaved samples in one frame (<see cref="SamplesPerChannel"/> × <see cref="Channels"/>).</summary>
    public int SamplesPerFrame => SamplesPerChannel * Channels;

    /// <summary>The canonical radio voice format: 48 kHz, mono, 20 ms (960 samples per channel).</summary>
    public static AudioFormat Voice { get; } = new(48_000, 1, 20);

    public override string ToString() => $"{SampleRateHz} Hz / {Channels}ch / {FrameMilliseconds} ms";
}

/// <summary>Shared Opus constants.</summary>
public static class OpusConstants
{
    /// <summary>
    /// Recommended upper bound for a single Opus packet (RFC 6716 guidance). Size encode output
    /// buffers to at least this; a pooled buffer of this size never overflows for one frame.
    /// </summary>
    public const int RecommendedMaxPacketBytes = 4_000;
}
