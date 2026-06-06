using Dasim.Radio.Contracts;
using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Degrade;
using Xunit;

namespace Dasim.Radio.MediaService.Tests;

public sealed class DegradeRegistryTests
{
    private readonly DegradeRegistry _sut = new();

    private static DegradeCommand Command(string listener, int quality, int clarity) =>
        new(listener, NetId: null, quality, clarity);

    [Fact]
    public void Apply_then_TryGetProfile_returns_the_profile()
    {
        _sut.Apply(Command("L1", 50, 40));

        Assert.True(_sut.TryGetProfile(new ParticipantId("L1"), out DegradeProfile profile));
        Assert.Equal(50, profile.QualityPercent);
        Assert.Equal(40, profile.ClarityPercent);
    }

    [Fact]
    public void A_clean_command_clears_an_existing_profile()
    {
        _sut.Apply(Command("L1", 50, 40));
        _sut.Apply(Command("L1", 100, 100));

        Assert.False(_sut.TryGetProfile(new ParticipantId("L1"), out _));
    }

    [Fact]
    public void An_unknown_listener_has_no_profile()
    {
        Assert.False(_sut.TryGetProfile(new ParticipantId("ghost"), out _));
    }

    [Fact]
    public void Out_of_range_values_are_clamped()
    {
        _sut.Apply(Command("L1", 150, -10));

        Assert.True(_sut.TryGetProfile(new ParticipantId("L1"), out DegradeProfile profile));
        Assert.Equal(100, profile.QualityPercent);
        Assert.Equal(0, profile.ClarityPercent);
    }

    [Fact]
    public void The_latest_command_wins()
    {
        _sut.Apply(Command("L1", 50, 50));
        _sut.Apply(Command("L1", 20, 30));

        _sut.TryGetProfile(new ParticipantId("L1"), out DegradeProfile profile);
        Assert.Equal(20, profile.QualityPercent);
        Assert.Equal(30, profile.ClarityPercent);
    }
}
