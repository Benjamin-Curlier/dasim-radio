using Dasim.Radio.Core;
using Xunit;

namespace Dasim.Radio.Core.Tests;

public sealed class MixPolicyTests
{
    private static MixSource Source(string speaker, int priority) =>
        new(new ParticipantId(speaker), new NetId($"net-{speaker}"), new Priority(priority));

    [Fact]
    public void Additive_returns_every_candidate()
    {
        var candidates = new List<MixSource> { Source("a", 10), Source("b", 20) };

        Assert.Equal(candidates, new AdditiveMixPolicy().Select(candidates));
    }

    [Fact]
    public void Additive_passes_an_empty_set_through()
    {
        Assert.Empty(new AdditiveMixPolicy().Select([]));
    }

    [Fact]
    public void Override_returns_only_the_highest_priority_candidate()
    {
        var candidates = new List<MixSource> { Source("a", 10), Source("b", 30), Source("c", 20) };

        MixSource winner = Assert.Single(new PriorityOverrideMixPolicy().Select(candidates));

        Assert.Equal("b", winner.Speaker.Value);
    }

    [Fact]
    public void Override_breaks_priority_ties_on_the_speaker_id()
    {
        var candidates = new List<MixSource> { Source("zulu", 50), Source("alpha", 50) };

        MixSource winner = Assert.Single(new PriorityOverrideMixPolicy().Select(candidates));

        Assert.Equal("alpha", winner.Speaker.Value);
    }

    [Fact]
    public void Override_passes_a_single_candidate_through()
    {
        var candidates = new List<MixSource> { Source("a", 10) };

        Assert.Equal(candidates, new PriorityOverrideMixPolicy().Select(candidates));
    }

    [Fact]
    public void Override_passes_an_empty_set_through()
    {
        Assert.Empty(new PriorityOverrideMixPolicy().Select([]));
    }

    [Fact]
    public void Highest_returns_the_top_priority_source()
    {
        var sources = new List<MixSource> { Source("a", 10), Source("b", 30), Source("c", 20) };

        Assert.Equal("b", MixSources.Highest(sources).Speaker.Value);
    }

    [Fact]
    public void Highest_breaks_ties_on_the_speaker_id()
    {
        var sources = new List<MixSource> { Source("zulu", 50), Source("alpha", 50) };

        Assert.Equal("alpha", MixSources.Highest(sources).Speaker.Value);
    }

    [Fact]
    public void Highest_throws_on_an_empty_set()
    {
        Assert.Throws<ArgumentException>(() => MixSources.Highest([]));
    }
}
