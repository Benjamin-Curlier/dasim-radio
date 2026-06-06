using Dasim.Radio.MediaService.Degrade;
using Xunit;

namespace Dasim.Radio.MediaService.Tests;

public sealed class DegradeProfileTests
{
    [Theory]
    [InlineData(150, -5, 100, 0)]
    [InlineData(-1, 101, 0, 100)]
    [InlineData(50, 40, 50, 40)]
    public void From_clamps_each_axis_into_0_to_100(int quality, int clarity, int expectedQuality, int expectedClarity)
    {
        DegradeProfile profile = DegradeProfile.From(quality, clarity);

        Assert.Equal(expectedQuality, profile.QualityPercent);
        Assert.Equal(expectedClarity, profile.ClarityPercent);
    }

    [Theory]
    [InlineData(100, 100, true)]
    [InlineData(100, 99, false)]
    [InlineData(99, 100, false)]
    [InlineData(0, 0, false)]
    public void IsClean_is_true_only_when_both_axes_are_full(int quality, int clarity, bool expected)
    {
        Assert.Equal(expected, new DegradeProfile(quality, clarity).IsClean);
    }
}
