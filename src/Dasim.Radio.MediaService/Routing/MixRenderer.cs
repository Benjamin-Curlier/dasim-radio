using Dasim.Radio.Audio;
using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Degrade;

namespace Dasim.Radio.MediaService.Routing;

/// <summary>One listener's output for a frame: the Opus bytes to publish to their <c>audio.out</c>.</summary>
public readonly record struct RenderedFrame(ParticipantId Listener, ReadOnlyMemory<byte> Opus);

/// <summary>
/// Turns one captured frame into each recipient's output. An undegraded listener gets the source bytes
/// untouched — a zero-transcode pass-through, the common case. A degraded listener gets the source
/// decoded once (shared), run through the clarity DSP, and re-encoded at their quality. Holds a decoder
/// per speaker and an encoder per degraded listener (Opus is stateful), and reuses scratch buffers, so
/// it is single-stream: drive it from one consumer (the media-router host does).
/// </summary>
public sealed class MixRenderer : IDisposable
{
    private static readonly AudioFormat Format = AudioFormat.Voice;

    private readonly IOpusDecoderFactory _decoderFactory;
    private readonly IOpusEncoderFactory _encoderFactory;
    private readonly IDegradeRegistry _degrade;
    private readonly ClarityProcessor _clarity;

    private readonly Dictionary<ParticipantId, IOpusDecoder> _decoders = [];
    private readonly Dictionary<ParticipantId, ListenerStream> _listeners = [];

    private readonly short[] _sourcePcm = new short[Format.SamplesPerFrame];
    private readonly short[] _workPcm = new short[Format.SamplesPerFrame];
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

    /// <summary>Produces the per-listener output for one captured frame from <paramref name="speaker"/>.</summary>
    public IReadOnlyList<RenderedFrame> Render(
        ParticipantId speaker, ReadOnlyMemory<byte> sourceOpus, IReadOnlyList<ParticipantId> recipients)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        if (recipients.Count == 0)
        {
            return [];
        }

        var output = new List<RenderedFrame>(recipients.Count);

        // Partition: pass-through now, collect the degraded for a shared decode + per-listener encode.
        // An empty source frame can't be decoded, so everyone gets it verbatim.
        List<(ParticipantId Listener, DegradeProfile Profile)>? degraded = null;
        foreach (ParticipantId listener in recipients)
        {
            if (!sourceOpus.IsEmpty && _degrade.TryGetProfile(listener, out DegradeProfile profile))
            {
                (degraded ??= []).Add((listener, profile));
            }
            else
            {
                output.Add(new RenderedFrame(listener, sourceOpus));
            }
        }

        if (degraded is null)
        {
            return output;
        }

        IOpusDecoder decoder = DecoderFor(speaker);
        decoder.Decode(sourceOpus.Span, _sourcePcm);

        foreach ((ParticipantId listener, DegradeProfile profile) in degraded)
        {
            ListenerStream stream = StreamFor(listener, profile.QualityPercent);

            _sourcePcm.CopyTo(_workPcm.AsSpan());
            _clarity.Apply(_workPcm, profile.ClarityPercent, ref stream.LowPassState);

            int written = stream.Encoder.Encode(_workPcm, _encodeBuffer);
            output.Add(new RenderedFrame(listener, _encodeBuffer.AsMemory(0, written).ToArray()));
        }

        return output;
    }

    public void Dispose()
    {
        foreach (IOpusDecoder decoder in _decoders.Values)
        {
            decoder.Dispose();
        }

        foreach (ListenerStream stream in _listeners.Values)
        {
            stream.Encoder.Dispose();
        }

        _decoders.Clear();
        _listeners.Clear();
    }

    private IOpusDecoder DecoderFor(ParticipantId speaker)
    {
        if (!_decoders.TryGetValue(speaker, out IOpusDecoder? decoder))
        {
            decoder = _decoderFactory.Create(Format);
            _decoders[speaker] = decoder;
        }

        return decoder;
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

    private sealed class ListenerStream
    {
        public required IOpusEncoder Encoder { get; set; }

        public required int Quality { get; set; }

        // A field (not a property) so the clarity low-pass state can be passed by ref.
        public float LowPassState;
    }
}
