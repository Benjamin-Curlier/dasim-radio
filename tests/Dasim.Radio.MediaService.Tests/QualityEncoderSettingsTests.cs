using Dasim.Radio.Audio;
using Dasim.Radio.MediaService.Degrade;
using Xunit;

namespace Dasim.Radio.MediaService.Tests;

public sealed class QualityEncoderSettingsTests
{
    [Theory]
    [InlineData(100, 24_000, 5)]
    [InlineData(50, 15_000, 2)]
    [InlineData(0, 6_000, 0)]
    public void Maps_quality_to_bitrate_and_complexity(int quality, int expectedBitrate, int expectedComplexity)
    {
        OpusEncoderSettings settings = QualityEncoderSettings.ForQuality(quality);

        Assert.Equal(expectedBitrate, settings.BitrateBitsPerSecond);
        Assert.Equal(expectedComplexity, settings.Complexity);
        Assert.Equal(OpusApplication.Voip, settings.Application);
    }

    [Fact]
    public void Clamps_out_of_range_quality()
    {
        Assert.Equal(24_000, QualityEncoderSettings.ForQuality(200).BitrateBitsPerSecond);
        Assert.Equal(6_000, QualityEncoderSettings.ForQuality(-5).BitrateBitsPerSecond);
    }

    [Fact]
    public void Produces_opus_legal_settings()
    {
        // Validate() throws if any value is out of Opus's legal range.
        QualityEncoderSettings.ForQuality(0).Validate();
        QualityEncoderSettings.ForQuality(100).Validate();
    }
}
