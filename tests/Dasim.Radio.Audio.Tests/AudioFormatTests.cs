using Dasim.Radio.Audio;
using Xunit;

namespace Dasim.Radio.Audio.Tests;

public sealed class AudioFormatTests
{
    [Fact]
    public void Voice_default_is_48k_mono_20ms()
    {
        AudioFormat f = AudioFormat.Voice;

        Assert.Equal(48_000, f.SampleRateHz);
        Assert.Equal(1, f.Channels);
        Assert.Equal(20d, f.FrameMilliseconds);
        Assert.Equal(960, f.SamplesPerChannel);
        Assert.Equal(960, f.SamplesPerFrame);
    }

    [Fact]
    public void Stereo_frame_interleaves_both_channels()
    {
        var f = new AudioFormat(48_000, 2, 20);

        Assert.Equal(960, f.SamplesPerChannel);
        Assert.Equal(1_920, f.SamplesPerFrame);
    }

    [Theory]
    [InlineData(2.5, 120)]
    [InlineData(10.0, 480)]
    [InlineData(60.0, 2_880)]
    public void Frame_duration_maps_to_sample_count(double ms, int samplesPerChannel)
    {
        Assert.Equal(samplesPerChannel, new AudioFormat(48_000, 1, ms).SamplesPerChannel);
    }

    [Theory]
    [InlineData(44_100)]
    [InlineData(0)]
    public void Rejects_unsupported_sample_rate(int rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioFormat(rate, 1, 20));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Rejects_unsupported_channel_count(int channels)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioFormat(48_000, channels, 20));
    }

    [Theory]
    [InlineData(15.0)]
    [InlineData(0.0)]
    public void Rejects_unsupported_frame_duration(double ms)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioFormat(48_000, 1, ms));
    }
}
