using Dasim.Radio.Contracts;
using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Degrade;
using Dasim.Radio.MediaService.Routing;
using Xunit;

namespace Dasim.Radio.MediaService.Tests;

public sealed class MixRendererTests
{
    private readonly FakeOpusDecoderFactory _decoders = new();
    private readonly FakeOpusEncoderFactory _encoders = new();
    private readonly DegradeRegistry _degrade = new();

    private MixRenderer NewRenderer() =>
        new(_decoders, _encoders, _degrade, new ClarityProcessor(new Random(1)));

    private static ParticipantId P(string id) => new(id);

    private static DegradeCommand Degrade(string listener, int quality, int clarity) =>
        new(listener, NetId: null, quality, clarity);

    [Fact]
    public void Without_a_profile_listeners_get_the_source_unchanged()
    {
        using MixRenderer sut = NewRenderer();
        var source = new byte[] { 1, 2, 3 };

        IReadOnlyList<RenderedFrame> rendered = sut.Render(P("spk"), source, [P("L1"), P("L2")]);

        Assert.Equal(2, rendered.Count);
        Assert.All(rendered, frame => Assert.True(frame.Opus.Span.SequenceEqual(source)));
        Assert.Empty(_decoders.Created); // pure pass-through never touches the codec
        Assert.Empty(_encoders.Created);
    }

    [Fact]
    public void A_degraded_listener_gets_a_transcoded_frame()
    {
        _degrade.Apply(Degrade("L1", quality: 50, clarity: 50));
        using MixRenderer sut = NewRenderer();

        RenderedFrame frame = Assert.Single(sut.Render(P("spk"), new byte[] { 7, 8 }, [P("L1")]));

        Assert.Equal(P("L1"), frame.Listener);
        Assert.Equal(FakeOpusEncoder.Marker, frame.Opus.Span[0]); // re-encoded, not the source bytes
        Assert.Single(_decoders.Created);
        FakeOpusEncoder encoder = Assert.Single(_encoders.Created);
        Assert.Equal(15_000, encoder.Settings.BitrateBitsPerSecond); // quality 50
    }

    [Fact]
    public void Degraded_and_pass_through_listeners_share_a_single_decode()
    {
        _degrade.Apply(Degrade("L1", 40, 60)); // L1 degraded, L2 clean
        using MixRenderer sut = NewRenderer();
        var source = new byte[] { 5 };

        IReadOnlyList<RenderedFrame> rendered = sut.Render(P("spk"), source, [P("L1"), P("L2")]);

        RenderedFrame degraded = rendered.Single(f => f.Listener == P("L1"));
        RenderedFrame passthrough = rendered.Single(f => f.Listener == P("L2"));
        Assert.Equal(FakeOpusEncoder.Marker, degraded.Opus.Span[0]);
        Assert.True(passthrough.Opus.Span.SequenceEqual(source));
        Assert.Single(_decoders.Created); // decoded once, reused for the degraded listener
    }

    [Fact]
    public void A_clean_profile_is_pass_through()
    {
        _degrade.Apply(Degrade("L1", 100, 100)); // clean -> registry drops it
        using MixRenderer sut = NewRenderer();
        var source = new byte[] { 1 };

        RenderedFrame frame = Assert.Single(sut.Render(P("spk"), source, [P("L1")]));

        Assert.True(frame.Opus.Span.SequenceEqual(source));
        Assert.Empty(_encoders.Created);
    }

    [Fact]
    public void Changing_quality_rebuilds_the_encoder()
    {
        using MixRenderer sut = NewRenderer();

        _degrade.Apply(Degrade("L1", 80, 100));
        sut.Render(P("spk"), new byte[] { 1 }, [P("L1")]);
        _degrade.Apply(Degrade("L1", 40, 100));
        sut.Render(P("spk"), new byte[] { 1 }, [P("L1")]);

        Assert.Equal(2, _encoders.Created.Count);
        Assert.Equal(
            QualityEncoderSettings.ForQuality(80).BitrateBitsPerSecond,
            _encoders.Created[0].Settings.BitrateBitsPerSecond);
        Assert.Equal(
            QualityEncoderSettings.ForQuality(40).BitrateBitsPerSecond,
            _encoders.Created[1].Settings.BitrateBitsPerSecond);
    }

    [Fact]
    public void An_empty_source_is_passed_through_even_to_a_degraded_listener()
    {
        _degrade.Apply(Degrade("L1", 50, 50));
        using MixRenderer sut = NewRenderer();

        RenderedFrame frame = Assert.Single(sut.Render(P("spk"), ReadOnlyMemory<byte>.Empty, [P("L1")]));

        Assert.True(frame.Opus.IsEmpty);
        Assert.Empty(_decoders.Created); // nothing to decode
    }

    [Fact]
    public void No_recipients_produces_no_output()
    {
        using MixRenderer sut = NewRenderer();

        Assert.Empty(sut.Render(P("spk"), new byte[] { 1 }, []));
    }
}
