namespace Dasim.Radio.Audio;

/// <summary>
/// Converts between interleaved 16-bit PCM (the radio's wire/codec format) and 32-bit float PCM (what
/// most device backends, e.g. OwnAudioSharp, capture and play). Conversion is sample-wise and
/// allocation-free into a caller-provided span.
/// </summary>
public static class PcmConvert
{
    // Scale a float sample (nominally [-1, 1]) by 32768 then clamp: -1.0 -> -32768, +1.0 -> +32767.
    private const float Scale = 32768f;

    /// <summary>Converts one float sample to 16-bit PCM, clamping out-of-range values.</summary>
    public static short ToShort(float sample)
    {
        int scaled = (int)MathF.Round(sample * Scale);
        return (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
    }

    /// <summary>Converts one 16-bit PCM sample to a float in [-1, 1).</summary>
    public static float ToFloat(short sample) => sample / Scale;

    /// <summary>Converts a float buffer to 16-bit PCM. <paramref name="destination"/> must be at least as long as <paramref name="source"/>.</summary>
    public static void FloatsToShorts(ReadOnlySpan<float> source, Span<short> destination)
    {
        if (destination.Length < source.Length)
        {
            throw new ArgumentException("Destination is shorter than the source.", nameof(destination));
        }

        for (int i = 0; i < source.Length; i++)
        {
            destination[i] = ToShort(source[i]);
        }
    }

    /// <summary>Converts a 16-bit PCM buffer to float. <paramref name="destination"/> must be at least as long as <paramref name="source"/>.</summary>
    public static void ShortsToFloats(ReadOnlySpan<short> source, Span<float> destination)
    {
        if (destination.Length < source.Length)
        {
            throw new ArgumentException("Destination is shorter than the source.", nameof(destination));
        }

        for (int i = 0; i < source.Length; i++)
        {
            destination[i] = ToFloat(source[i]);
        }
    }
}
