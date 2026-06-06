using BenchmarkDotNet.Attributes;
using Dasim.Radio.Audio;

namespace Dasim.Radio.Audio.Benchmarks;

/// <summary>
/// Isolates the clarity DSP the media service runs per degraded mix (one-pole low-pass + additive
/// noise over a 960-sample frame) to decide finding #7: is <see cref="Random.Shared"/>'s
/// <c>NextDouble()</c> + <see cref="Math.Round(double)"/>, called 960×/frame, worth replacing with a
/// cheap xorshift on local <see cref="uint"/> state? <see cref="RandomShared"/> is the current code;
/// <see cref="Xorshift"/> is the candidate. Both produce the same shape of output (band-limited +
/// dithered), so this measures only the per-sample arithmetic cost.
/// </summary>
[MemoryDiagnoser]
public class ClarityBenchmarks
{
    private const float MinAlpha = 0.05f;
    private const double MaxNoiseFraction = 0.15;

    private static readonly AudioFormat Format = AudioFormat.Voice;

    [Params(50)]
    public int ClarityPercent { get; set; }

    private short[] _pcm = [];
    private readonly Random _random = new(12345);
    private uint _state = 0x9E3779B9;
    private float _lowPass;

    [GlobalSetup]
    public void Setup()
    {
        _pcm = new short[Format.SamplesPerFrame];
        for (int i = 0; i < _pcm.Length; i++)
        {
            _pcm[i] = (short)(Math.Sin(2 * Math.PI * 440 * i / Format.SampleRateHz) * 0.3 * short.MaxValue);
        }
    }

    /// <summary>The current implementation: <see cref="Random"/> + <see cref="Math.Round(double)"/>.</summary>
    [Benchmark(Baseline = true)]
    public void RandomShared()
    {
        int clarity = ClarityPercent;
        float alpha = MinAlpha + ((1f - MinAlpha) * (clarity / 100f));
        double noiseAmplitude = (100 - clarity) / 100.0 * MaxNoiseFraction * short.MaxValue;

        float state = _lowPass;
        Span<short> pcm = _pcm;
        for (int i = 0; i < pcm.Length; i++)
        {
            state += alpha * (pcm[i] - state);
            double noisy = state + (((_random.NextDouble() * 2.0) - 1.0) * noiseAmplitude);
            pcm[i] = (short)Math.Clamp((int)Math.Round(noisy), short.MinValue, short.MaxValue);
        }

        _lowPass = state;
    }

    /// <summary>The candidate: xorshift on local uint state, integer truncation instead of rounding.</summary>
    [Benchmark]
    public void Xorshift()
    {
        int clarity = ClarityPercent;
        float alpha = MinAlpha + ((1f - MinAlpha) * (clarity / 100f));
        float noiseAmplitude = (float)((100 - clarity) / 100.0 * MaxNoiseFraction * short.MaxValue);

        float state = _lowPass;
        uint rng = _state;
        Span<short> pcm = _pcm;
        for (int i = 0; i < pcm.Length; i++)
        {
            state += alpha * (pcm[i] - state);

            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            // Map to [-1, 1): take the top 24 bits as a float in [0,1), then scale.
            float unit = ((rng >> 8) * (1.0f / 16_777_216.0f) * 2f) - 1f;

            int sample = (int)(state + (unit * noiseAmplitude));
            pcm[i] = (short)Math.Clamp(sample, short.MinValue, short.MaxValue);
        }

        _lowPass = state;
        _state = rng;
    }
}
