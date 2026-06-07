using Dasim.Radio.Audio;
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
        new(_decoders, _encoders, _degrade, new ClarityProcessor(noiseSeed: 1));

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
    public void Changing_quality_retunes_the_encoder_in_place()
    {
        using MixRenderer sut = NewRenderer();
        sut.Remember(P("spk"), new byte[] { 1 });

        _degrade.Apply(Degrade("L1", 80, 100));
        sut.Render([Delivery("L1", "spk")]);
        _degrade.Apply(Degrade("L1", 40, 100));
        sut.Render([Delivery("L1", "spk")]);

        // The quality change re-tunes the existing encoder (keeping Opus state) rather than rebuilding it.
        FakeOpusEncoder encoder = Assert.Single(_encoders.Created);
        OpusEncoderSettings retune = Assert.Single(encoder.Retunes);
        Assert.Equal(QualityEncoderSettings.ForQuality(40).BitrateBitsPerSecond, retune.BitrateBitsPerSecond);
        Assert.Equal(QualityEncoderSettings.ForQuality(40).Complexity, retune.Complexity);
        // The encoder was created at the original quality, then mutated to the new one.
        Assert.Equal(QualityEncoderSettings.ForQuality(40).BitrateBitsPerSecond, encoder.Settings.BitrateBitsPerSecond);
    }

    [Fact]
    public void Listeners_with_an_identical_profile_share_one_encode()
    {
        _degrade.Apply(Degrade("L1", 50, 60));
        _degrade.Apply(Degrade("L2", 50, 60));
        using MixRenderer sut = NewRenderer();
        sut.Remember(P("spk"), new byte[] { 9 });

        IReadOnlyList<RenderedFrame> frames = sut.Render([Delivery("L1", "spk"), Delivery("L2", "spk")]);

        Assert.Equal(2, frames.Count);
        // One encoder for the shared profile, and both listeners get the very same bytes.
        Assert.Single(_encoders.Created);
        Assert.True(frames[0].Opus.Span.SequenceEqual(frames[1].Opus.Span));
    }

    [Fact]
    public void Listeners_with_different_quality_do_not_share_an_encode()
    {
        _degrade.Apply(Degrade("L1", 50, 60));
        _degrade.Apply(Degrade("L2", 30, 60)); // different quality => different profile
        using MixRenderer sut = NewRenderer();
        sut.Remember(P("spk"), new byte[] { 9 });

        sut.Render([Delivery("L1", "spk"), Delivery("L2", "spk")]);

        Assert.Equal(2, _encoders.Created.Count); // a separate encode per distinct profile
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

    [Fact]
    public void Consecutive_cycles_each_render_correctly_with_reused_buffers()
    {
        // The renderer reuses its scratch/output buffers across cycles (single-consumer contract). Each
        // cycle's output must be consumed before the next Render; once it is, the reuse must not corrupt
        // the following cycle. Two transcoded cycles with different content prove that. Quality<100 forces
        // the transcode path; clarity 100 keeps the DSP a no-op so the echoed sample is deterministic.
        _degrade.Apply(Degrade("L1", 50, 100));
        using MixRenderer sut = NewRenderer();

        sut.Remember(P("spk"), new byte[] { 10 });
        RenderedFrame first = Assert.Single(sut.Render([Delivery("L1", "spk")]));
        byte firstEcho = first.Opus.Span[1]; // the fake encoder echoes the first PCM sample

        sut.Remember(P("spk"), new byte[] { 99 });
        RenderedFrame second = Assert.Single(sut.Render([Delivery("L1", "spk")]));
        byte secondEcho = second.Opus.Span[1];

        // Different source bytes -> different decoded PCM -> different encoded echo, proving the second
        // cycle rendered fresh content rather than re-serving the first cycle's reused buffer.
        Assert.NotEqual(firstEcho, secondEcho);
        Assert.Single(_encoders.Created); // the steady profile kept its one (re)used encoder
    }

    [Fact]
    public void An_idle_profiles_encoder_is_evicted_and_disposed()
    {
        _degrade.Apply(Degrade("L1", 50, 100));
        using MixRenderer sut = NewRenderer();
        sut.Remember(P("a"), new byte[] { 1 });
        sut.Remember(P("b"), new byte[] { 2 });

        // First cycle builds the encoder for profile {a}; it then goes idle as L1 switches to hearing b.
        sut.Render([Delivery("L1", "a")]);
        FakeOpusEncoder forA = _encoders.Created[0];

        // A different source set => no migration; profile {a} idles. Drive past the eviction window.
        for (int i = 0; i < 260; i++)
        {
            sut.Render([Delivery("L1", "b")]);
        }

        Assert.True(forA.Disposed, "the idle profile's encoder should have been evicted and disposed");
    }

    [Fact]
    public void An_idle_sources_decoder_is_evicted_and_disposed()
    {
        _degrade.Apply(Degrade("L1", 50, 100)); // force the transcode path so the source builds a decoder
        using MixRenderer sut = NewRenderer();
        sut.Remember(P("a"), new byte[] { 1 });

        // First cycle decodes source a, building its decoder; a then goes silent (never Remembered again).
        sut.Render([Delivery("L1", "a")]);
        FakeOpusDecoder forA = _decoders.Created[0];

        // A different speaker keeps publishing while a idles. Drive past the eviction window.
        for (int i = 0; i < 260; i++)
        {
            sut.Remember(P("b"), new byte[] { 2 });
            sut.Render([Delivery("L1", "b")]);
        }

        Assert.True(forA.Disposed, "the idle source's decoder should have been evicted and disposed");
        Assert.False(_decoders.Created[1].Disposed, "the still-active source's decoder must be kept");
    }
}
