using Dasim.Radio.Audio;

namespace Dasim.Radio.MediaService.Degrade;

/// <summary>
/// Maps the <em>quality</em> half of degradation (0–100) to Opus encoder settings: lower quality means
/// a lower bitrate and lower complexity, i.e. a coarser, more compressed re-encode. Quality 100 is
/// the media service's normal encode.
/// </summary>
public static class QualityEncoderSettings
{
    /// <summary>Bitrate at quality 0 — heavily compressed but still intelligible.</summary>
    public const int MinBitrateBitsPerSecond = 6_000;

    /// <summary>Bitrate at quality 100 — the normal LAN voice bitrate.</summary>
    public const int MaxBitrateBitsPerSecond = 24_000;

    /// <summary>Encoder complexity at quality 100 (kept modest: the media service runs many encodes).</summary>
    public const int MaxComplexity = 5;

    /// <summary>Builds the encoder settings for a quality level, clamping it into 0–100.</summary>
    public static OpusEncoderSettings ForQuality(int qualityPercent)
    {
        int quality = Math.Clamp(qualityPercent, 0, 100);
        int bitrate = MinBitrateBitsPerSecond
            + (quality * (MaxBitrateBitsPerSecond - MinBitrateBitsPerSecond) / 100);
        int complexity = quality * MaxComplexity / 100;

        return new OpusEncoderSettings
        {
            Application = OpusApplication.Voip,
            BitrateBitsPerSecond = bitrate,
            Complexity = complexity,
        };
    }
}
