using Dasim.Radio.Audio;
using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Degrade;

namespace Dasim.Radio.MediaService.Routing;

/// <summary>One listener's output for a frame: the Opus bytes to publish to their <c>audio.out</c>.</summary>
public readonly record struct RenderedFrame(ParticipantId Listener, ReadOnlyMemory<byte> Opus);

/// <summary>
/// Renders each listener's output from the latest frame of every speaker. Call <see cref="Remember"/>
/// for every captured frame, then <see cref="Render"/> with the deliveries the router triggered.
/// <list type="bullet">
/// <item>A single undegraded source is forwarded untouched — a zero-transcode pass-through.</item>
/// <item>Otherwise the sources are decoded, summed, optionally degraded, and re-encoded — but
/// <em>once per shared profile</em>: listeners with the same ordered source set, quality and clarity
/// share a single encode whose bytes are fanned out to all of them.</item>
/// </list>
/// Each speaker's frame is decoded at most once per cycle (a lazy "stale" flag), so an undegraded
/// pass-through never decodes and a speaker summed into several listeners is decoded once. Holds a
/// decoder per speaker and an encoder per <em>shared profile</em> (Opus is stateful); a profile whose
/// quality changes is re-tuned in place rather than rebuilt, so the stream stays continuous. Reuses
/// all scratch and output buffers, so it is single-stream: drive it from one consumer.
/// </summary>
public sealed class MixRenderer : IDisposable
{
    private static readonly AudioFormat Format = AudioFormat.Voice;

    // How long (in render cycles) an idle profile's encoder is kept before disposal, so a brief gap in
    // a speaker's audio (e.g. DTX) does not throw away the encoder's state. 250 cycles ≈ 5 s at 50 fps.
    private const long IdleCyclesBeforeEvict = 250;

    private readonly IOpusDecoderFactory _decoderFactory;
    private readonly IOpusEncoderFactory _encoderFactory;
    private readonly IDegradeRegistry _degrade;
    private readonly ClarityProcessor _clarity;

    private readonly Dictionary<ParticipantId, SourceFrame> _sources = [];
    private readonly Dictionary<EncodeProfile, ProfileStream> _streams = [];

    // Per-cycle scratch, reused across frames (single consumer — see the type remarks).
    private readonly List<RenderedFrame> _output = [];
    private readonly Dictionary<EncodeProfile, int> _groupIndex = [];
    private readonly List<Group> _groups = [];
    private readonly List<byte[]> _frameBuffers = [];
    private readonly List<EncodeProfile> _staleKeys = [];

    private readonly int[] _mix = new int[Format.SamplesPerFrame];
    private readonly short[] _work = new short[Format.SamplesPerFrame];

    private long _cycle;

    public MixRenderer(
        IOpusDecoderFactory decoderFactory,
        IOpusEncoderFactory encoderFactory,
        IDegradeRegistry degrade,
        ClarityProcessor clarity)
    {
        _decoderFactory = decoderFactory ?? throw new ArgumentNullException(nameof(decoderFactory));
        _encoderFactory = encoderFactory ?? throw new ArgumentNullException(nameof(encoderFactory));
        _degrade = degrade ?? throw new ArgumentNullException(nameof(degrade));
        _clarity = clarity ?? throw new ArgumentNullException(nameof(clarity));
    }

    /// <summary>Records a speaker's latest captured frame. Decoding is deferred until a mix needs it.</summary>
    public void Remember(ParticipantId speaker, ReadOnlyMemory<byte> opus)
    {
        if (!_sources.TryGetValue(speaker, out SourceFrame? frame))
        {
            frame = new SourceFrame();
            _sources[speaker] = frame;
        }

        frame.Opus = opus;
        frame.PcmValid = false;
    }

    /// <summary>
    /// Produces the output frame for each triggered listener. The returned list is reused across calls
    /// and its frames alias internal buffers, so consume it before the next <see cref="Render"/>.
    /// </summary>
    public IReadOnlyList<RenderedFrame> Render(IReadOnlyList<MixDelivery> deliveries)
    {
        ArgumentNullException.ThrowIfNull(deliveries);
        _output.Clear();
        if (deliveries.Count == 0)
        {
            return _output;
        }

        _cycle++;
        int groupCount = GroupDeliveries(deliveries);

        for (int g = 0; g < groupCount; g++)
        {
            RenderGroup(_groups[g], g);
        }

        EvictIdleStreams();
        return _output;
    }

    public void Dispose()
    {
        foreach (SourceFrame source in _sources.Values)
        {
            source.Decoder?.Dispose();
        }

        foreach (ProfileStream stream in _streams.Values)
        {
            stream.Encoder.Dispose();
        }

        _sources.Clear();
        _streams.Clear();
    }

    // Splits this cycle's deliveries into zero-transcode pass-throughs (emitted directly) and shared
    // transcode groups keyed by (ordered source set, quality, clarity), returning the count of active
    // groups. The _groups pool is reused across cycles — only the first groupCount entries are live;
    // any left over from a busier cycle stay pooled (and untouched) for reuse.
    private int GroupDeliveries(IReadOnlyList<MixDelivery> deliveries)
    {
        _groupIndex.Clear();
        int groupCount = 0;

        // Index, don't foreach: iterating an IReadOnlyList-typed value boxes the enumerator each cycle.
        for (int d = 0; d < deliveries.Count; d++)
        {
            MixDelivery delivery = deliveries[d];
            IReadOnlyList<MixSource> sources = delivery.Sources;
            bool degraded = _degrade.TryGetProfile(delivery.Listener, out DegradeProfile profile);

            // Fast path: a single undegraded source is forwarded untouched (and trivially shared — every
            // such listener of the same speaker references the same source bytes).
            if (sources.Count == 1 && !degraded)
            {
                if (_sources.TryGetValue(sources[0].Speaker, out SourceFrame? only))
                {
                    _output.Add(new RenderedFrame(delivery.Listener, only.Opus));
                }

                continue;
            }

            int quality = degraded ? profile.QualityPercent : 100;
            int clarity = degraded ? profile.ClarityPercent : 100;
            var key = EncodeProfile.For(sources, quality, clarity, delivery.Listener);

            if (!_groupIndex.TryGetValue(key, out int index))
            {
                index = groupCount++;
                Group group = RentGroup(index);
                group.Reset(key, sources, quality, clarity);
                _groupIndex[key] = index;
            }

            _groups[index].Listeners.Add(delivery.Listener);
        }

        return groupCount;
    }

    private void RenderGroup(Group group, int index)
    {
        Array.Clear(_mix);
        bool any = false;
        IReadOnlyList<MixSource> sources = group.Sources;
        for (int s = 0; s < sources.Count; s++)
        {
            short[]? pcm = EnsurePcm(sources[s].Speaker);
            if (pcm is null)
            {
                continue;
            }

            for (int i = 0; i < Format.SamplesPerFrame; i++)
            {
                _mix[i] += pcm[i];
            }

            any = true;
        }

        if (!any)
        {
            return;
        }

        for (int i = 0; i < Format.SamplesPerFrame; i++)
        {
            _work[i] = (short)Math.Clamp(_mix[i], short.MinValue, short.MaxValue);
        }

        ProfileStream stream = StreamFor(group.Key, group.Quality);
        if (group.Clarity < 100)
        {
            _clarity.Apply(_work, group.Clarity, ref stream.LowPassState);
        }

        byte[] buffer = RentFrameBuffer(index);
        int written = stream.Encoder.Encode(_work, buffer);
        var opus = new ReadOnlyMemory<byte>(buffer, 0, written);

        foreach (ParticipantId listener in group.Listeners)
        {
            _output.Add(new RenderedFrame(listener, opus));
        }
    }

    private short[]? EnsurePcm(ParticipantId speaker)
    {
        if (!_sources.TryGetValue(speaker, out SourceFrame? frame) || frame.Opus.IsEmpty)
        {
            return null;
        }

        if (!frame.PcmValid)
        {
            frame.Decoder ??= _decoderFactory.Create(Format);
            frame.Decoder.Decode(frame.Opus.Span, frame.Pcm);
            frame.PcmValid = true;
        }

        return frame.Pcm;
    }

    // Returns the encoder + clarity state for a shared profile, creating it on first use. When a
    // profile's quality changes its key changes too; rather than build a fresh encoder (which resets
    // Opus state and clicks), an orphaned stream for the same source set is re-tuned in place and
    // re-keyed — the stream stays continuous across the quality change.
    private ProfileStream StreamFor(EncodeProfile key, int quality)
    {
        if (_streams.TryGetValue(key, out ProfileStream? stream))
        {
            stream.LastCycle = _cycle;
            return stream;
        }

        ProfileStream? reused = FindReusableStream(key);
        if (reused is not null)
        {
            _streams.Remove(reused.Key);
            if (reused.Quality != quality)
            {
                reused.Encoder.Retune(QualityEncoderSettings.ForQuality(quality));
                reused.Quality = quality;
            }

            reused.Key = key;
            reused.LastCycle = _cycle;
            _streams[key] = reused;
            return reused;
        }

        stream = new ProfileStream
        {
            Key = key,
            Quality = quality,
            Encoder = _encoderFactory.Create(Format, QualityEncoderSettings.ForQuality(quality)),
            LastCycle = _cycle,
        };
        _streams[key] = stream;
        return stream;
    }

    // An orphan (untouched this cycle) for the same source set: the quality and/or clarity changed but
    // the audio content did not, so its encoder state is worth carrying over via a re-tune. If several
    // same-source orphans exist (e.g. two old quality levels), which one is reused is unspecified — all
    // encoded the same audio, so any gives equivalent continuity.
    private ProfileStream? FindReusableStream(EncodeProfile key)
    {
        foreach (ProfileStream candidate in _streams.Values)
        {
            if (candidate.LastCycle != _cycle && candidate.Key.SameSourcesAs(key))
            {
                return candidate;
            }
        }

        return null;
    }

    private void EvictIdleStreams()
    {
        _staleKeys.Clear();
        foreach ((EncodeProfile key, ProfileStream stream) in _streams)
        {
            if (_cycle - stream.LastCycle > IdleCyclesBeforeEvict)
            {
                _staleKeys.Add(key);
            }
        }

        foreach (EncodeProfile key in _staleKeys)
        {
            if (_streams.Remove(key, out ProfileStream? stream))
            {
                stream.Encoder.Dispose();
            }
        }
    }

    private Group RentGroup(int index)
    {
        while (_groups.Count <= index)
        {
            _groups.Add(new Group());
        }

        return _groups[index];
    }

    private byte[] RentFrameBuffer(int index)
    {
        while (_frameBuffers.Count <= index)
        {
            _frameBuffers.Add(new byte[OpusConstants.RecommendedMaxPacketBytes]);
        }

        return _frameBuffers[index];
    }

    /// <summary>
    /// The identity of a shared encode: the ordered source set plus the quality and clarity applied.
    /// Two listeners with an equal profile receive byte-identical output, so a single encode serves
    /// them all. The source set is held canonically (≤2 sources, ordinal-sorted) — the bound the
    /// subtree-net model guarantees; a listener with more sources (outside that model) carries a unique
    /// discriminator so it is never wrongly merged.
    /// </summary>
    private readonly record struct EncodeProfile(
        ParticipantId Source0,
        ParticipantId Source1,
        int SourceCount,
        int Quality,
        int Clarity,
        ParticipantId Discriminator)
    {
        public static EncodeProfile For(
            IReadOnlyList<MixSource> sources, int quality, int clarity, ParticipantId listener)
        {
            switch (sources.Count)
            {
                case 1:
                    return new EncodeProfile(sources[0].Speaker, default, 1, quality, clarity, default);
                case 2:
                    ParticipantId a = sources[0].Speaker;
                    ParticipantId b = sources[1].Speaker;
                    if (string.CompareOrdinal(a.Value, b.Value) > 0)
                    {
                        (a, b) = (b, a);
                    }

                    return new EncodeProfile(a, b, 2, quality, clarity, default);
                default:
                    // Outside the subtree-net model (>2, or 0 which never reaches here): key on the
                    // listener so the encode is correct (summed from the real source list) but unshared.
                    return new EncodeProfile(default, default, sources.Count, quality, clarity, listener);
            }
        }

        /// <summary>True when the source set matches, ignoring quality/clarity (a re-tune candidate).</summary>
        public bool SameSourcesAs(EncodeProfile other) =>
            SourceCount == other.SourceCount &&
            Source0 == other.Source0 &&
            Source1 == other.Source1 &&
            Discriminator == other.Discriminator;
    }

    private sealed class SourceFrame
    {
        public short[] Pcm { get; } = new short[Format.SamplesPerFrame];

        public ReadOnlyMemory<byte> Opus { get; set; }

        public bool PcmValid { get; set; }

        // Created lazily on first decode, so an undegraded pass-through never builds a decoder.
        public IOpusDecoder? Decoder { get; set; }
    }

    // The listeners sharing one encode this cycle, plus the source set and degradation to apply.
    // Reused across cycles; Reset re-points it without allocating.
    private sealed class Group
    {
        public EncodeProfile Key { get; private set; }

        public IReadOnlyList<MixSource> Sources { get; private set; } = [];

        public int Quality { get; private set; }

        public int Clarity { get; private set; }

        public List<ParticipantId> Listeners { get; } = [];

        public void Reset(EncodeProfile key, IReadOnlyList<MixSource> sources, int quality, int clarity)
        {
            Key = key;
            Sources = sources;
            Quality = quality;
            Clarity = clarity;
            Listeners.Clear();
        }
    }

    // The persistent encoder + clarity low-pass state for one shared profile, carried across cycles so
    // a steady profile keeps continuous Opus state.
    private sealed class ProfileStream
    {
        public required IOpusEncoder Encoder { get; set; }

        public required EncodeProfile Key { get; set; }

        public required int Quality { get; set; }

        public required long LastCycle { get; set; }

        // A field (not a property) so the clarity low-pass state can be passed by ref.
        public float LowPassState;
    }
}
