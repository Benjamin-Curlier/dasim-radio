namespace Dasim.Radio.MediaService.Degrade;

/// <summary>
/// Applies the <em>clarity</em> half of degradation to a frame of mono 16-bit PCM in place: a one-pole
/// low-pass that muffles the audio plus additive white noise (static). Clarity 100 is a clean no-op;
/// lower clarity muffles harder and adds more static. Not thread-safe (one frame at a time); the noise
/// source is injectable so tests are deterministic.
/// </summary>
public sealed class ClarityProcessor
{
    // The lowest low-pass coefficient, so even clarity 0 still lets some signal through (not pure static).
    private const float MinAlpha = 0.05f;

    // The additive-noise amplitude at clarity 0, as a fraction of full scale.
    private const double MaxNoiseFraction = 0.15;

    private readonly Random _noise;

    public ClarityProcessor(Random? noise = null) => _noise = noise ?? Random.Shared;

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
        float alpha = MinAlpha + (1f - MinAlpha) * (clarity / 100f);
        double noiseAmplitude = (100 - clarity) / 100.0 * MaxNoiseFraction * short.MaxValue;

        float state = lowPassState;
        for (int i = 0; i < pcm.Length; i++)
        {
            state += alpha * (pcm[i] - state);
            double noisy = state + (_noise.NextDouble() * 2.0 - 1.0) * noiseAmplitude;
            pcm[i] = (short)Math.Clamp((int)Math.Round(noisy), short.MinValue, short.MaxValue);
        }

        lowPassState = state;
    }
}
