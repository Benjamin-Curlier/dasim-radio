namespace Dasim.Radio.MediaService.Degrade;

/// <summary>
/// Applies the <em>clarity</em> half of degradation to a frame of mono 16-bit PCM in place: a one-pole
/// low-pass that muffles the audio plus additive white noise (static). Clarity 100 is a clean no-op;
/// lower clarity muffles harder and adds more static. Not thread-safe (one frame at a time).
/// </summary>
/// <remarks>
/// The static is generated with a cheap xorshift on local <see cref="uint"/> state rather than
/// <see cref="Random"/> + <see cref="Math.Round(double)"/>: the DSP runs once per degraded mix, 960
/// samples a frame, and a benchmark put the xorshift at ~⅓ the cost with no perceptible difference for
/// dither. The seed is injectable so tests stay deterministic.
/// </remarks>
public sealed class ClarityProcessor
{
    // The lowest low-pass coefficient, so even clarity 0 still lets some signal through (not pure static).
    private const float MinAlpha = 0.05f;

    // The additive-noise amplitude at clarity 0, as a fraction of full scale.
    private const double MaxNoiseFraction = 0.15;

    // A non-zero default seed (xorshift must never reach an all-zero state).
    private const uint DefaultSeed = 0x9E3779B9;

    private uint _noiseState;

    public ClarityProcessor(uint noiseSeed = DefaultSeed) =>
        _noiseState = noiseSeed == 0 ? DefaultSeed : noiseSeed;

    /// <summary>
    /// Degrades <paramref name="pcm"/> in place to <paramref name="clarityPercent"/> (0–100). The
    /// low-pass carries its last output across calls via <paramref name="lowPassState"/> (one per
    /// stream) so frame boundaries don't click.
    /// </summary>
    public void Apply(Span<short> pcm, int clarityPercent, ref float lowPassState)
    {
        if (clarityPercent >= 100)
        {
            return; // clean
        }

        int clarity = Math.Clamp(clarityPercent, 0, 100);
        float alpha = MinAlpha + ((1f - MinAlpha) * (clarity / 100f));
        float noiseAmplitude = (float)((100 - clarity) / 100.0 * MaxNoiseFraction * short.MaxValue);

        float state = lowPassState;
        uint rng = _noiseState;
        for (int i = 0; i < pcm.Length; i++)
        {
            state += alpha * (pcm[i] - state);

            // xorshift32, then map the top 24 bits to a float in [-1, 1) for the dither.
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            float dither = ((rng >> 8) * (1.0f / 16_777_216.0f) * 2f) - 1f;

            pcm[i] = (short)Math.Clamp((int)(state + (dither * noiseAmplitude)), short.MinValue, short.MaxValue);
        }

        lowPassState = state;
        _noiseState = rng;
    }
}
