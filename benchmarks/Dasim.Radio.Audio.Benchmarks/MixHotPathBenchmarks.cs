using BenchmarkDotNet.Attributes;
using Dasim.Radio.Audio;
using Dasim.Radio.Contracts;
using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Degrade;
using Dasim.Radio.MediaService.Routing;

namespace Dasim.Radio.Audio.Benchmarks;

/// <summary>
/// Measures the media-service per-frame mix hot path — <see cref="MediaRouter.Deliveries"/> plus
/// <see cref="MixRenderer.Render"/> — over a ~50-participant force tree with many simultaneous floor
/// holders. <see cref="Tick"/> replays one 20 ms cycle: every active speaker's frame is remembered,
/// routed to its listeners, and rendered. The point of this benchmark is the <b>managed allocation</b>
/// per tick (the routing/mix orchestration the realtime-audio-reviewer flagged), so it deliberately
/// uses a non-allocating <see cref="FakeCodec"/> — that way <see cref="MemoryDiagnoserAttribute"/>
/// attributes every byte to our code, not to Concentus/libopus internals. Real encode/decode CPU is
/// covered separately by <see cref="OpusEncodeBenchmarks"/>.
///
/// <para>Run: <c>dotnet run -c Release --project benchmarks/Dasim.Radio.Audio.Benchmarks --
/// --filter *MixHotPath*</c>.</para>
/// </summary>
[MemoryDiagnoser]
public class MixHotPathBenchmarks
{
    public enum Combine
    {
        Override,
        Additive,
    }

    [Params(Combine.Override, Combine.Additive)]
    public Combine Policy { get; set; }

    /// <summary>Percentage of listeners under an active degrade profile (forces the transcode path).</summary>
    [Params(0, 50)]
    public int DegradePercent { get; set; }

    private static readonly AudioFormat Format = AudioFormat.Voice;

    private MediaRouter _router = null!;
    private MixRenderer _renderer = null!;
    private ParticipantId[] _speakers = [];
    private byte[][] _frames = [];

    [GlobalSetup]
    public void Setup()
    {
        ForceTree tree = BuildForce();
        NetTopology topology = NetTopology.FromForceTree(tree);
        ForceRouting routing = ForceRouting.Create(1, tree, topology);
        var force = new FixedForceTreeProvider(routing);

        // Drive the floor exactly as the host would, so the holder snapshot is authentic.
        var floor = new FloorControlService(TimeProvider.System);
        var speakers = new List<ParticipantId>();

        foreach (ForceNode node in tree.Enumerate())
        {
            if (node.Children.Count == 0)
            {
                continue;
            }

            var net = new NetId(node.Id);
            if (node.Children[0].Children.Count == 0)
            {
                // A net whose children are leaf members: a member talks UP (low priority).
                ForceNode member = node.Children[0];
                floor.RequestFloor(net, new ParticipantId(member.Id), member.Priority);
                speakers.Add(new ParticipantId(member.Id));
            }
            else
            {
                // A net over sub-nets: the owner talks DOWN (its own, higher priority).
                floor.RequestFloor(net, new ParticipantId(node.Id), node.Priority);
                speakers.Add(new ParticipantId(node.Id));
            }
        }

        IMixPolicy policy = Policy == Combine.Additive
            ? new AdditiveMixPolicy()
            : new PriorityOverrideMixPolicy();
        _router = new MediaRouter(force, new FloorControlHolders(floor), policy);

        var degrade = new DegradeRegistry();
        ApplyDegradation(tree, degrade);

        _renderer = new MixRenderer(
            new FakeCodecFactory(), new FakeCodecFactory(), degrade, new ClarityProcessor(noiseSeed: 12345));

        _speakers = [.. speakers];
        _frames = new byte[_speakers.Length][];
        for (int i = 0; i < _speakers.Length; i++)
        {
            // The fake decoder reads byte 0; a distinct value per speaker keeps the sums non-trivial.
            _frames[i] = [(byte)(i + 1), 0x55];
            _renderer.Remember(_speakers[i], _frames[i]);
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _renderer.Dispose();

    /// <summary>One 20 ms cycle: every active speaker's frame routed and rendered to its listeners.</summary>
    [Benchmark]
    public long Tick()
    {
        long total = 0;
        for (int i = 0; i < _speakers.Length; i++)
        {
            _renderer.Remember(_speakers[i], _frames[i]);
            IReadOnlyList<MixDelivery> deliveries = _router.Deliveries(_speakers[i]);
            IReadOnlyList<RenderedFrame> rendered = _renderer.Render(deliveries);
            for (int r = 0; r < rendered.Count; r++)
            {
                total += rendered[r].Opus.Length;
            }
        }

        return total;
    }

    private void ApplyDegradation(ForceTree tree, DegradeRegistry degrade)
    {
        if (DegradePercent <= 0)
        {
            return;
        }

        int index = 0;
        int step = Math.Max(1, 100 / DegradePercent);
        foreach (ForceNode node in tree.Enumerate())
        {
            if (index++ % step == 0)
            {
                degrade.Apply(new DegradeCommand(node.Id, NetId: null, QualityPercent: 50, ClarityPercent: 60));
            }
        }
    }

    // CO ─ 3 companies ─ 2 sections each ─ 2 groups each ─ 3 members each = 58 participants, 22 nets.
    private static ForceTree BuildForce()
    {
        var companies = new List<ForceNode>();
        for (int c = 0; c < 3; c++)
        {
            var sections = new List<ForceNode>();
            for (int s = 0; s < 2; s++)
            {
                var groups = new List<ForceNode>();
                for (int g = 0; g < 2; g++)
                {
                    var members = new List<ForceNode>();
                    for (int m = 0; m < 3; m++)
                    {
                        string mid = $"c{c}s{s}g{g}m{m}";
                        members.Add(ForceNode.Leaf(mid, mid, ForceNodeKind.Member, new Priority(20)));
                    }

                    string gid = $"c{c}s{s}g{g}";
                    groups.Add(new ForceNode(gid, gid, ForceNodeKind.Group, new Priority(40), members));
                }

                string sid = $"c{c}s{s}";
                sections.Add(new ForceNode(sid, sid, ForceNodeKind.Section, new Priority(60), groups));
            }

            string cid = $"c{c}";
            companies.Add(new ForceNode(cid, cid, ForceNodeKind.Company, new Priority(80), sections));
        }

        var co = new ForceNode("co", "co", ForceNodeKind.Command, new Priority(100), companies);
        return new ForceTree(co);
    }

    private sealed class FixedForceTreeProvider(ForceRouting routing) : IForceTreeProvider
    {
        public ForceRouting Current { get; } = routing;
    }

    /// <summary>A non-allocating stand-in codec: decode fills a constant, encode writes a fixed marker.</summary>
    private sealed class FakeCodec : IOpusEncoder, IOpusDecoder
    {
        public AudioFormat Format => MixHotPathBenchmarks.Format;

        public int Encode(ReadOnlySpan<short> pcm, Span<byte> output)
        {
            output[0] = 0xEE;
            output[1] = (byte)(pcm[0] & 0xFF);
            return 2;
        }

        public void Retune(OpusEncoderSettings settings)
        {
        }

        public int Decode(ReadOnlySpan<byte> opus, Span<short> pcm)
        {
            pcm[..MixHotPathBenchmarks.Format.SamplesPerFrame].Fill(opus.Length > 0 ? opus[0] : (short)0);
            return MixHotPathBenchmarks.Format.SamplesPerChannel;
        }

        public int DecodeFec(ReadOnlySpan<byte> nextPacket, Span<short> pcm) => Decode(nextPacket, pcm);

        public int DecodeLost(Span<short> pcm)
        {
            pcm[..MixHotPathBenchmarks.Format.SamplesPerFrame].Clear();
            return MixHotPathBenchmarks.Format.SamplesPerChannel;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeCodecFactory : IOpusEncoderFactory, IOpusDecoderFactory
    {
        public IOpusEncoder Create(AudioFormat format, OpusEncoderSettings? settings = null) => new FakeCodec();

        IOpusDecoder IOpusDecoderFactory.Create(AudioFormat format) => new FakeCodec();
    }
}
