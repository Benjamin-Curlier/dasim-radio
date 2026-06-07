using System.Diagnostics;
using Dasim.Radio.LossProbe;
using Xunit;

namespace Dasim.Radio.LossProbe.Tests;

public sealed class LossAnalyzerTests
{
    private static readonly long FrameTicks = Stopwatch.Frequency / 50; // ~20 ms

    [Fact]
    public void Empty_analyzer_reports_no_data()
    {
        LossReport report = new LossAnalyzer().Build(sameClock: false);

        Assert.Equal(0, report.Observed);
        Assert.Equal(0, report.Lost);
        Assert.Equal(0, report.Expected);
        Assert.Contains("NO DATA", report.Verdict, StringComparison.Ordinal);
    }

    [Fact]
    public void Clean_stream_has_no_loss()
    {
        LossReport report = Feed(Range(0, 100)).Build(sameClock: false);

        Assert.Equal(100, report.Expected);
        Assert.Equal(100, report.DistinctReceived);
        Assert.Equal(0, report.Lost);
        Assert.Equal(0, report.GapCount);
        Assert.Equal(0, report.LossPercent);
        Assert.Contains("NEGLIGIBLE", report.Verdict, StringComparison.Ordinal);
    }

    [Fact]
    public void Isolated_single_frame_gaps_are_fully_recoverable()
    {
        // 0..99 with four single-frame holes — the case Opus FEC is built for.
        uint[] present = [.. Range(0, 100).Where(s => s is not (10 or 30 or 50 or 70))];

        LossReport report = Feed(present).Build(sameClock: false);

        Assert.Equal(100, report.Expected);
        Assert.Equal(4, report.Lost);
        Assert.Equal(4, report.GapCount);
        Assert.Equal(4, report.IsolatedLostFrames);
        Assert.Equal(1.0, report.IsolatedFractionOfLost, 3);
        Assert.Equal(1.0, report.MeanGapFrames, 3);
        Assert.Equal(4, report.BurstHistogram[1]);
        Assert.Contains("ISOLATED-DOMINATED", report.Verdict, StringComparison.Ordinal);
    }

    [Fact]
    public void A_burst_is_one_gap_and_is_not_isolated()
    {
        // A single five-frame burst (10..14): the TCP slow-consumer / reconnect signature.
        uint[] present = [.. Range(0, 100).Where(s => s is < 10 or > 14)];

        LossReport report = Feed(present).Build(sameClock: false);

        Assert.Equal(5, report.Lost);
        Assert.Equal(1, report.GapCount);
        Assert.Equal(0, report.IsolatedLostFrames);
        Assert.Equal(0.0, report.IsolatedFractionOfLost, 3);
        Assert.Equal(5.0, report.MeanGapFrames, 3);
        Assert.Equal(5, report.MaxGapFrames);
        Assert.Equal(1, report.BurstHistogram[5]);
        Assert.Contains("BURST-DOMINATED", report.Verdict, StringComparison.Ordinal);
    }

    [Fact]
    public void Mixed_loss_below_half_isolated_is_judged_bursty()
    {
        // One isolated hole (5) + one four-frame burst (20..23): 1/5 = 20% isolated.
        uint[] present = [.. Range(0, 100).Where(s => s != 5 && s is < 20 or > 23)];

        LossReport report = Feed(present).Build(sameClock: false);

        Assert.Equal(5, report.Lost);
        Assert.Equal(2, report.GapCount);
        Assert.Equal(1, report.IsolatedLostFrames);
        Assert.Equal(0.2, report.IsolatedFractionOfLost, 3);
        Assert.Contains("BURST-DOMINATED", report.Verdict, StringComparison.Ordinal);
    }

    [Fact]
    public void Frames_before_the_subscriber_joined_are_not_counted_as_loss()
    {
        // Subscriber joins late: it never sees 0..49, only 50..99. That's a join gap, not transit loss.
        LossReport report = Feed(Range(50, 50)).Build(sameClock: false);

        Assert.Equal(50, report.Expected);
        Assert.Equal(50, report.DistinctReceived);
        Assert.Equal(0, report.Lost);
    }

    [Fact]
    public void Reordering_is_counted_but_is_not_loss()
    {
        LossReport report = Feed([0, 1, 3, 2, 4]).Build(sameClock: false);

        Assert.Equal(0, report.Lost);
        Assert.Equal(1, report.Reordered);
        Assert.Equal(5, report.DistinctReceived);
    }

    [Fact]
    public void Duplicates_are_counted_but_are_not_loss()
    {
        LossReport report = Feed([0, 1, 1, 2]).Build(sameClock: false);

        Assert.Equal(1, report.Duplicates);
        Assert.Equal(3, report.DistinctReceived);
        Assert.Equal(3, report.Expected);
        Assert.Equal(0, report.Lost);
    }

    [Fact]
    public void One_way_latency_is_reported_only_with_a_shared_clock()
    {
        long latencyTicks = Stopwatch.Frequency / 200; // 5 ms
        var analyzer = new LossAnalyzer();
        long arrival = 1_000_000;
        for (uint s = 0; s < 50; s++)
        {
            analyzer.Observe(s, arrival, arrival - latencyTicks);
            arrival += FrameTicks;
        }

        Assert.Null(analyzer.Build(sameClock: false).Latency);

        LatencyStats? latency = analyzer.Build(sameClock: true).Latency;
        Assert.NotNull(latency);
        Assert.Equal(5.0, latency.Value.MeanMs, 1);
    }

    private static LossAnalyzer Feed(IEnumerable<uint> sequencesInArrivalOrder)
    {
        var analyzer = new LossAnalyzer();
        long arrival = 1_000_000;
        foreach (uint seq in sequencesInArrivalOrder)
        {
            analyzer.Observe(seq, arrival, arrival);
            arrival += FrameTicks;
        }

        return analyzer;
    }

    private static IEnumerable<uint> Range(uint start, uint count)
    {
        for (uint i = 0; i < count; i++)
        {
            yield return start + i;
        }
    }
}
