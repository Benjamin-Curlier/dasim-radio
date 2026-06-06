using Dasim.Radio.Audio;
using Xunit;

namespace Dasim.Radio.Audio.Tests;

public sealed class PcmConvertTests
{
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.5f, 16384)]
    [InlineData(-0.5f, -16384)]
    [InlineData(1f, 32767)]      // 1.0 * 32768 = 32768, clamped to short.MaxValue
    [InlineData(-1f, -32768)]
    [InlineData(2f, 32767)]      // out of range, clamped
    [InlineData(-2f, -32768)]    // out of range, clamped
    public void ToShort_scales_and_clamps(float input, short expected) =>
        Assert.Equal(expected, PcmConvert.ToShort(input));

    [Theory]
    [InlineData((short)0, 0f)]
    [InlineData(short.MinValue, -1f)]
    public void ToFloat_scales(short input, float expected) =>
        Assert.Equal(expected, PcmConvert.ToFloat(input), 5);

    [Fact]
    public void Floats_to_shorts_converts_each_sample()
    {
        float[] source = [0f, 0.5f, -0.5f];
        short[] destination = new short[3];

        PcmConvert.FloatsToShorts(source, destination);

        Assert.Equal([0, 16384, -16384], destination);
    }

    [Fact]
    public void Shorts_to_floats_round_trips_approximately()
    {
        short[] source = [0, 16384, -16384];
        float[] destination = new float[3];

        PcmConvert.ShortsToFloats(source, destination);

        Assert.Equal(0f, destination[0], 5);
        Assert.Equal(0.5f, destination[1], 5);
        Assert.Equal(-0.5f, destination[2], 5);
    }

    [Fact]
    public void A_short_destination_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => PcmConvert.FloatsToShorts(new float[4], new short[3]));
        Assert.Throws<ArgumentException>(() => PcmConvert.ShortsToFloats(new short[4], new float[3]));
    }
}
