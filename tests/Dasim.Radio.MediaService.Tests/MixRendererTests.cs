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

    private static MixSource Src(string speaker) => new(P(speaker), new NetId("n"), new Priority(0));

    private static MixDelivery Delivery(string listener, params string[] speakers) =>
        new(P(listener), [.. speakers.Select(Src)]);

    private static DegradeCommand Degrade(string listener, int quality, int clarity) =>
        new(listener, NetId: null, quality, clarity);

    [Fact]
    public void A_single_undegraded_source_is_passed_through()
    {
        using MixRenderer sut = NewRenderer();
        var source = new byte[] { 1, 2, 3 };
        sut.Remember(P("spk"), source);

        RenderedFrame frame = Assert.Single(sut.Render([Delivery("L1", "spk")]));

        Assert.True(frame.Opus.Span.SequenceEqual(source));
        Assert.Empty(_decoders.Created); // pass-through never touches the codec
        Assert.Empty(_encoders.Created);
    }

    [Fact]
    public void A_degraded_single_source_is_transcoded()
    {
        _degrade.Apply(Degrade("L1", quality: 50, clarity: 50));
        using MixRenderer sut = NewRenderer();
        sut.Remember(P("spk"), new byte[] { 7, 8 });

        RenderedFrame frame = Assert.Single(sut.Render([Delivery("L1", "spk")]));

        Assert.Equal(FakeOpusEncoder.Marker, frame.Opus.Span[0]);
        Assert.Single(_decoders.Created);
        Assert.Equal(15_000, Assert.Single(_encoders.Created).Settings.BitrateBitsPerSecond); // quality 50
    }

    [Fact]
    public void Two_sources_are_summed_and_re_encoded()
    {
        using MixRenderer sut = NewRenderer();
        sut.Remember(P("a"), new byte[] { 10 });
        sut.Remember(P("b"), new byte[] { 20 });

        RenderedFrame frame = Assert.Single(sut.Render([Delivery("L1", "a", "b")]));

        Assert.Equal(FakeOpusEncoder.Marker, frame.Opus.Span[0]); // a sum can't be passed through
        Assert.Equal(2, _decoders.Created.Count); // both sources decoded
        Assert.Equal(24_000, Assert.Single(_encoders.Created).Settings.BitrateBitsPerSecond); // undegraded mix = full quality
    }

    [Fact]
    public void A_speaker_summed_into_several_listeners_is_decoded_once()
    {
        using MixRenderer sut = NewRenderer();
        sut.Remember(P("a"), new byte[] { 10 });
        sut.Remember(P("b"), new byte[] { 20 });

        sut.Render([Delivery("L1", "a", "b"), Delivery("L2", "a", "b")]);

        Assert.Equal(2, _decoders.Created.Count); // a and b each decoded once, not per listener
    }

    [Fact]
    public void A_clean_profile_is_pass_through()
    {
        _degrade.Apply(Degrade("L1", 100, 100)); // clean -> registry drops it
        using MixRenderer sut = NewRenderer();
        var source = new byte[] { 1 };
        sut.Remember(P("spk"), source);

        RenderedFrame frame = Assert.Single(sut.Render([Delivery("L1", "spk")]));

        Assert.True(frame.Opus.Span.SequenceEqual(source));
        Assert.Empty(_encoders.Created);
    }

    [Fact]
    public void Changing_quality_rebuilds_the_encoder()
    {
        using MixRenderer sut = NewRenderer();
        sut.Remember(P("spk"), new byte[] { 1 });

        _degrade.Apply(Degrade("L1", 80, 100));
        sut.Render([Delivery("L1", "spk")]);
        _degrade.Apply(Degrade("L1", 40, 100));
        sut.Render([Delivery("L1", "spk")]);

        Assert.Equal(2, _encoders.Created.Count);
        Assert.Equal(
            QualityEncoderSettings.ForQuality(80).BitrateBitsPerSecond,
            _encoders.Created[0].Settings.BitrateBitsPerSecond);
        Assert.Equal(
            QualityEncoderSettings.ForQuality(40).BitrateBitsPerSecond,
            _encoders.Created[1].Settings.BitrateBitsPerSecond);
    }

    [Fact]
    public void An_empty_source_to_a_degraded_listener_produces_nothing()
    {
        _degrade.Apply(Degrade("L1", 50, 50));
        using MixRenderer sut = NewRenderer();
        sut.Remember(P("spk"), ReadOnlyMemory<byte>.Empty);

        Assert.Empty(sut.Render([Delivery("L1", "spk")]));
        Assert.Empty(_decoders.Created); // nothing to decode
    }

    [Fact]
    public void No_deliveries_produces_no_output()
    {
        using MixRenderer sut = NewRenderer();

        Assert.Empty(sut.Render([]));
    }
}
