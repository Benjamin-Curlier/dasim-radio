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
/// <item>Otherwise the sources are decoded, summed, optionally degraded, and re-encoded per listener.</item>
/// </list>
/// Each speaker's frame is decoded at most once per cycle (a lazy "stale" flag), so an undegraded
/// pass-through never decodes and a speaker summed into several listeners is decoded once. Holds a
/// decoder per speaker and an encoder per transcoded listener (Opus is stateful) and reuses scratch
/// buffers, so it is single-stream: drive it from one consumer.
/// </summary>
public sealed class MixRenderer : IDisposable
{
    private static readonly AudioFormat Format = AudioFormat.Voice;

    private readonly IOpusDecoderFactory _decoderFactory;
    private readonly IOpusEncoderFactory _encoderFactory;
    private readonly IDegradeRegistry _degrade;
    private readonly ClarityProcessor _clarity;

    private readonly Dictionary<ParticipantId, SourceFrame> _sources = [];
    private readonly Dictionary<ParticipantId, ListenerStream> _listeners = [];

    private readonly int[] _mix = new int[Format.SamplesPerFrame];
    private readonly short[] _work = new short[Format.SamplesPerFrame];
    private readonly byte[] _encodeBuffer = new byte[OpusConstants.RecommendedMaxPacketBytes];

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

    /// <summary>Produces the output frame for each triggered listener.</summary>
    public IReadOnlyList<RenderedFrame> Render(IReadOnlyList<MixDelivery> deliveries)
    {
        ArgumentNullException.ThrowIfNull(deliveries);
        if (deliveries.Count == 0)
        {
            return [];
        }

        var output = new List<RenderedFrame>(deliveries.Count);
        foreach (MixDelivery delivery in deliveries)
        {
            if (RenderOne(delivery) is { } frame)
            {
                output.Add(frame);
            }
        }

        return output;
    }

    public void Dispose()
    {
        foreach (SourceFrame source in _sources.Values)
        {
            source.Decoder?.Dispose();
        }

        foreach (ListenerStream stream in _listeners.Values)
        {
            stream.Encoder.Dispose();
        }

        _sources.Clear();
        _listeners.Clear();
    }

    private RenderedFrame? RenderOne(MixDelivery delivery)
    {
        ParticipantId listener = delivery.Listener;
        IReadOnlyList<MixSource> sources = delivery.Sources;
        bool degraded = _degrade.TryGetProfile(listener, out DegradeProfile profile);

        // Fast path: a single undegraded source is forwarded untouched.
        if (sources.Count == 1 && !degraded)
        {
            return _sources.TryGetValue(sources[0].Speaker, out SourceFrame? only)
                ? new RenderedFrame(listener, only.Opus)
                : null;
        }

        Array.Clear(_mix);
        bool any = false;
        foreach (MixSource source in sources)
        {
            short[]? pcm = EnsurePcm(source.Speaker);
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
            return null;
        }

        for (int i = 0; i < Format.SamplesPerFrame; i++)
        {
            _work[i] = (short)Math.Clamp(_mix[i], short.MinValue, short.MaxValue);
        }

        int quality = degraded ? profile.QualityPercent : 100;
        ListenerStream stream = StreamFor(listener, quality);
        if (degraded)
        {
            _clarity.Apply(_work, profile.ClarityPercent, ref stream.LowPassState);
        }

        int written = stream.Encoder.Encode(_work, _encodeBuffer);
        return new RenderedFrame(listener, _encodeBuffer.AsMemory(0, written).ToArray());
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

    private ListenerStream StreamFor(ParticipantId listener, int qualityPercent)
    {
        if (_listeners.TryGetValue(listener, out ListenerStream? stream))
        {
            if (stream.Quality != qualityPercent)
            {
                // Opus bitrate/complexity are fixed at creation, so a quality change rebuilds the encoder.
                stream.Encoder.Dispose();
                stream.Encoder = _encoderFactory.Create(Format, QualityEncoderSettings.ForQuality(qualityPercent));
                stream.Quality = qualityPercent;
            }

            return stream;
        }

        stream = new ListenerStream
        {
            Encoder = _encoderFactory.Create(Format, QualityEncoderSettings.ForQuality(qualityPercent)),
            Quality = qualityPercent,
        };
        _listeners[listener] = stream;
        return stream;
    }

    private sealed class SourceFrame
    {
        public short[] Pcm { get; } = new short[Format.SamplesPerFrame];

        public ReadOnlyMemory<byte> Opus { get; set; }

        public bool PcmValid { get; set; }

        // Created lazily on first decode, so an undegraded pass-through never builds a decoder.
        public IOpusDecoder? Decoder { get; set; }
    }

    private sealed class ListenerStream
    {
        public required IOpusEncoder Encoder { get; set; }

        public required int Quality { get; set; }

        // A field (not a property) so the clarity low-pass state can be passed by ref.
        public float LowPassState;
    }
}
