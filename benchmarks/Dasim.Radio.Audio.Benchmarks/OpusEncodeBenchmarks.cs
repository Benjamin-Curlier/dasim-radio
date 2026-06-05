using BenchmarkDotNet.Attributes;
using Dasim.Radio.Audio;
using Dasim.Radio.Audio.Concentus;
using Dasim.Radio.Audio.Opus;

namespace Dasim.Radio.Audio.Benchmarks;

/// <summary>
/// De-risks the Option-B media service: can one host produce the per-listener Opus streams it owes
/// every 20 ms tick? <see cref="EncodeBatch"/> times a whole tick on ONE core, while
/// <see cref="EncodeBatchParallel"/> fans the same work across the thread pool — the production
/// pattern, and the only honest multi-core number (do NOT divide the single-core result by cores;
/// shared LLC/memory bandwidth and allocator contention make that optimistic).
/// <see cref="EncodeSingle"/> gives the precise per-encode cost.
///
/// Read before quoting any number:
/// <list type="bullet">
/// <item><see cref="BatchSize"/> = 50 is a deliberate STRESS CEILING, not the expected per-tick
/// count. Strict floor control means ~one talker holds the floor per net, and the media service
/// shares a single encode across listeners with the same (net-set + degradation) profile, so the
/// real number of distinct encodes per tick is materially lower.</item>
/// <item>Each iteration replays the same frames, so the encoder reaches a steady rate-control state
/// that continuously-varying voice never does — this biases the per-op cost optimistic.</item>
/// <item><see cref="MemoryDiagnoserAttribute"/> sees MANAGED allocations only; native libopus
/// (OpusSharp) heap use is invisible. A "-" for OpusSharp means "no managed scratch", not "no
/// malloc"; for managed Concentus the number is the real signal.</item>
/// </list>
/// Run on the REAL deployment CPU with realistic input before sizing hosts:
/// <c>dotnet run -c Release --project benchmarks/Dasim.Radio.Audio.Benchmarks</c>.
/// </summary>
[MemoryDiagnoser]
public class OpusEncodeBenchmarks
{
    /// <summary>Distinct encodes a media-service host may run per 20 ms tick (worst case).</summary>
    private const int BatchSize = 50;

    public enum CodecKind
    {
        Concentus,
        OpusSharp,
    }

    [Params(CodecKind.Concentus, CodecKind.OpusSharp)]
    public CodecKind Codec { get; set; }

    [Params(10, 5)]
    public int Complexity { get; set; }

    private readonly AudioFormat _format = AudioFormat.Voice;
    private short[] _pcm = [];
    private byte[] _packet = [];
    private byte[] _encodedFrame = [];
    private short[] _decodeOut = [];
    private int _encodedLength;

    private IOpusEncoder _encoder = null!;
    private IOpusDecoder _decoder = null!;
    private IOpusEncoder[] _batchEncoders = [];
    private short[][] _batchPcm = [];
    private byte[][] _batchPackets = [];

    [GlobalSetup]
    public void Setup()
    {
        IOpusEncoderFactory encoderFactory = Codec == CodecKind.Concentus
            ? new ConcentusOpusEncoderFactory()
            : new OpusSharpEncoderFactory();
        IOpusDecoderFactory decoderFactory = Codec == CodecKind.Concentus
            ? new ConcentusOpusDecoderFactory()
            : new OpusSharpDecoderFactory();

        var settings = new OpusEncoderSettings { Complexity = Complexity };

        _pcm = Sine(_format, 440);
        _packet = new byte[OpusConstants.RecommendedMaxPacketBytes];
        _decodeOut = new short[_format.SamplesPerFrame];

        _encoder = encoderFactory.Create(_format, settings);
        _decoder = decoderFactory.Create(_format);

        _encodedFrame = new byte[OpusConstants.RecommendedMaxPacketBytes];
        _encodedLength = _encoder.Encode(_pcm, _encodedFrame);

        _batchEncoders = new IOpusEncoder[BatchSize];
        _batchPcm = new short[BatchSize][];
        _batchPackets = new byte[BatchSize][];
        for (int i = 0; i < BatchSize; i++)
        {
            _batchEncoders[i] = encoderFactory.Create(_format, settings);
            _batchPcm[i] = Sine(_format, 300 + (i * 10)); // distinct content per stream
            _batchPackets[i] = new byte[OpusConstants.RecommendedMaxPacketBytes];
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _encoder.Dispose();
        _decoder.Dispose();
        foreach (IOpusEncoder encoder in _batchEncoders)
        {
            encoder.Dispose();
        }
    }

    /// <summary>One 20 ms frame encoded — the precise per-stream cost.</summary>
    [Benchmark]
    public int EncodeSingle() => _encoder.Encode(_pcm, _packet);

    /// <summary>One 20 ms frame decoded.</summary>
    [Benchmark]
    public int DecodeSingle() => _decoder.Decode(_encodedFrame.AsSpan(0, _encodedLength), _decodeOut);

    /// <summary>A full tick: <see cref="BatchSize"/> independent encodes on one core.</summary>
    [Benchmark]
    public int EncodeBatch()
    {
        int total = 0;
        for (int i = 0; i < BatchSize; i++)
        {
            total += _batchEncoders[i].Encode(_batchPcm[i], _batchPackets[i]);
        }

        return total;
    }

    /// <summary>
    /// A full tick fanned across the thread pool — the production pattern. Each stream has its own
    /// encoder, so no two threads touch the same (stateful, non-thread-safe) encoder at once.
    /// </summary>
    [Benchmark]
    public void EncodeBatchParallel() =>
        Parallel.For(0, BatchSize, i => _batchEncoders[i].Encode(_batchPcm[i], _batchPackets[i]));

    private static short[] Sine(AudioFormat format, double frequencyHz)
    {
        short[] pcm = new short[format.SamplesPerFrame];
        int perChannel = format.SamplesPerChannel;

        for (int i = 0; i < perChannel; i++)
        {
            double t = i / (double)format.SampleRateHz;
            short sample = (short)(Math.Sin(2 * Math.PI * frequencyHz * t) * 0.3 * short.MaxValue);

            for (int c = 0; c < format.Channels; c++)
            {
                pcm[(i * format.Channels) + c] = sample;
            }
        }

        return pcm;
    }
}
