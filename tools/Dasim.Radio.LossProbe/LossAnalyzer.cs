using System.Diagnostics;

namespace Dasim.Radio.LossProbe;

/// <summary>
/// Accumulates received probe frames and, on demand, computes the loss picture that decides whether
/// Opus FEC/PLC is worth adding to the data plane. The decisive output is not the raw loss rate but the
/// <em>shape</em> of the loss: a histogram of gap run-lengths, from which the isolated-loss fraction
/// (the only thing in-band FEC can recover) is derived.
/// </summary>
/// <remarks>
/// Frames are recorded exactly (a set of seen sequence numbers plus arrival timestamps in arrival
/// order) and analysed offline in <see cref="Build"/>; at probe rates (≈50 fps for a few minutes) the
/// few thousand entries are negligible, and exact bookkeeping avoids the subtle off-by-one bugs an
/// incremental gap counter invites. Not thread-safe — feed it from the single subscribe loop.
/// </remarks>
public sealed class LossAnalyzer
{
    private readonly HashSet<uint> _seen = [];
    private readonly List<long> _arrivalTicks = [];      // one entry per distinct sequence, in arrival order
    private readonly List<long> _oneWayLatencyTicks = []; // arrival - send, per distinct sequence (same-clock only)

    private bool _any;
    private uint _minSeq;
    private uint _maxSeq;
    private uint _lastArrivedSeq;
    private long _observed;
    private long _duplicates;
    private long _reordered;

    /// <summary>Total messages handed to <see cref="Observe"/> (including duplicates).</summary>
    public long Observed => _observed;

    /// <summary>Records one received probe frame.</summary>
    /// <param name="sequence">The frame's monotonic sequence number.</param>
    /// <param name="arrivalTimestampTicks"><see cref="Stopwatch.GetTimestamp"/> when it arrived.</param>
    /// <param name="sendTimestampTicks">The sender's stamp from the header (used for latency only).</param>
    public void Observe(uint sequence, long arrivalTimestampTicks, long sendTimestampTicks)
    {
        _observed++;

        if (_any && sequence < _lastArrivedSeq)
        {
            _reordered++;
        }

        _lastArrivedSeq = sequence;

        if (!_seen.Add(sequence))
        {
            _duplicates++;
            return; // a duplicate adds nothing to the gap/jitter picture
        }

        if (!_any)
        {
            _any = true;
            _minSeq = sequence;
            _maxSeq = sequence;
        }
        else
        {
            if (sequence < _minSeq)
            {
                _minSeq = sequence;
            }

            if (sequence > _maxSeq)
            {
                _maxSeq = sequence;
            }
        }

        _arrivalTicks.Add(arrivalTimestampTicks);
        _oneWayLatencyTicks.Add(arrivalTimestampTicks - sendTimestampTicks);
    }

    /// <summary>
    /// Computes the report. <paramref name="sameClock"/> gates the one-way latency block, which is only
    /// meaningful when publisher and subscriber share a clock (local mode / same host).
    /// </summary>
    public LossReport Build(bool sameClock)
    {
        if (!_any)
        {
            return LossReport.Empty;
        }

        // Baseline on the first sequence actually seen: frames published before the subscriber joined
        // are a join gap, not a transit loss, so they must not be counted against the link.
        long expected = (long)_maxSeq - _minSeq + 1;

        var burstHistogram = new SortedDictionary<int, int>();
        long lost = 0;
        long isolatedLostFrames = 0;
        int run = 0;

        // Walk the whole sequence span exactly once; a maximal run of unseen numbers is one gap (burst).
        for (long s = _minSeq; s <= _maxSeq; s++)
        {
            if (_seen.Contains((uint)s))
            {
                if (run > 0)
                {
                    RecordBurst(burstHistogram, run, ref isolatedLostFrames);
                    lost += run;
                    run = 0;
                }
            }
            else
            {
                run++;
            }
        }

        if (run > 0)
        {
            RecordBurst(burstHistogram, run, ref isolatedLostFrames);
            lost += run;
        }

        int gapCount = 0;
        foreach (int count in burstHistogram.Values)
        {
            gapCount += count;
        }

        InterArrival jitter = ComputeInterArrival();
        LatencyStats? latency = sameClock ? ComputeLatency() : null;

        return new LossReport(
            Observed: _observed,
            DistinctReceived: _seen.Count,
            Expected: expected,
            Lost: lost,
            IsolatedLostFrames: isolatedLostFrames,
            GapCount: gapCount,
            Duplicates: _duplicates,
            Reordered: _reordered,
            BurstHistogram: burstHistogram,
            Jitter: jitter,
            Latency: latency);
    }

    private static void RecordBurst(SortedDictionary<int, int> histogram, int runLength, ref long isolatedLostFrames)
    {
        histogram[runLength] = histogram.TryGetValue(runLength, out int c) ? c + 1 : 1;
        if (runLength == 1)
        {
            isolatedLostFrames++;
        }
    }

    private InterArrival ComputeInterArrival()
    {
        if (_arrivalTicks.Count < 2)
        {
            return InterArrival.Empty;
        }

        // Arrival deltas in arrival order. Sequence reordering is rare on TCP, so arrival order is a fair
        // proxy for the playback cadence the receiver would experience.
        var deltasMs = new double[_arrivalTicks.Count - 1];
        for (int i = 1; i < _arrivalTicks.Count; i++)
        {
            deltasMs[i - 1] = TicksToMs(_arrivalTicks[i] - _arrivalTicks[i - 1]);
        }

        Array.Sort(deltasMs);
        double sum = 0;
        foreach (double d in deltasMs)
        {
            sum += d;
        }

        double mean = sum / deltasMs.Length;
        return new InterArrival(
            MeanMs: mean,
            P50Ms: Percentile(deltasMs, 0.50),
            P95Ms: Percentile(deltasMs, 0.95),
            MaxMs: deltasMs[^1]);
    }

    private LatencyStats ComputeLatency()
    {
        var ms = new double[_oneWayLatencyTicks.Count];
        for (int i = 0; i < _oneWayLatencyTicks.Count; i++)
        {
            ms[i] = TicksToMs(_oneWayLatencyTicks[i]);
        }

        Array.Sort(ms);
        double sum = 0;
        foreach (double v in ms)
        {
            sum += v;
        }

        return new LatencyStats(
            MeanMs: sum / ms.Length,
            P50Ms: Percentile(ms, 0.50),
            P95Ms: Percentile(ms, 0.95),
            MaxMs: ms[^1]);
    }

    private static double Percentile(double[] sortedAscending, double quantile)
    {
        if (sortedAscending.Length == 0)
        {
            return 0;
        }

        int index = (int)Math.Ceiling(quantile * sortedAscending.Length) - 1;
        index = Math.Clamp(index, 0, sortedAscending.Length - 1);
        return sortedAscending[index];
    }

    private static double TicksToMs(long stopwatchTicks) =>
        stopwatchTicks * 1000.0 / Stopwatch.Frequency;
}
