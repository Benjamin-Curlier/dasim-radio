using Dasim.Radio.MediaService.Degrade;
using Xunit;

namespace Dasim.Radio.MediaService.Tests;

public sealed class ClarityProcessorTests
{
    // A fixed seed keeps the additive noise deterministic.
    private readonly ClarityProcessor _sut = new(new Random(1));

    [Fact]
    public void Clarity_100_is_a_no_op()
    {
        short[] pcm = [100, -200, 300, -400, 500];
        short[] original = (short[])pcm.Clone();
        float lowPass = 0f;

        _sut.Apply(pcm, 100, ref lowPass);

        Assert.Equal(original, pcm);
        Assert.Equal(0f, lowPass);
    }

    [Fact]
    public void Low_clarity_adds_static_to_silence()
    {
        short[] pcm = new short[960]; // silence
        float lowPass = 0f;

        _sut.Apply(pcm, 0, ref lowPass);

        Assert.Contains(pcm, sample => sample != 0);
    }

    [Fact]
    public void Low_clarity_attenuates_a_high_frequency_signal()
    {
        short[] pcm = new short[960];
        for (int i = 0; i < pcm.Length; i++)
        {
            pcm[i] = (short)(i % 2 == 0 ? 20_000 : -20_000); // Nyquist-rate alternation
        }

        float lowPass = 0f;
        _sut.Apply(pcm, 30, ref lowPass);

        int outputPeak = pcm.Max(sample => Math.Abs((int)sample));
        Assert.True(outputPeak < 20_000, $"expected the low-pass to attenuate the signal, peak was {outputPeak}.");
    }
}
