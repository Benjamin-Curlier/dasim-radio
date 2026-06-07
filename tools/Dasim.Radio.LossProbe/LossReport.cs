using System.Globalization;
using System.Text;

namespace Dasim.Radio.LossProbe;

/// <summary>Inter-arrival spacing of received frames, in milliseconds (the receiver's perceived cadence).</summary>
public readonly record struct InterArrival(double MeanMs, double P50Ms, double P95Ms, double MaxMs)
{
    public static InterArrival Empty => new(0, 0, 0, 0);
}

/// <summary>One-way latency, in milliseconds. Only computed when sender and receiver share a clock.</summary>
public readonly record struct LatencyStats(double MeanMs, double P50Ms, double P95Ms, double MaxMs);

/// <summary>
/// The computed loss picture for one probe run, plus a plain-English <see cref="Verdict"/> that applies
/// the FEC/PLC decision rule to the measured numbers.
/// </summary>
public sealed record LossReport(
    long Observed,
    int DistinctReceived,
    long Expected,
    long Lost,
    long IsolatedLostFrames,
    int GapCount,
    long Duplicates,
    long Reordered,
    IReadOnlyDictionary<int, int> BurstHistogram,
    InterArrival Jitter,
    LatencyStats? Latency)
{
    /// <summary>An all-zero report (nothing received).</summary>
    public static LossReport Empty { get; } = new(
        0, 0, 0, 0, 0, 0, 0, 0,
        new Dictionary<int, int>(), InterArrival.Empty, null);

    /// <summary>Frames lost as a percentage of the expected span (0 when nothing was expected).</summary>
    public double LossPercent => Expected > 0 ? 100.0 * Lost / Expected : 0;

    /// <summary>Fraction of <em>lost frames</em> that were isolated single-frame gaps (FEC-recoverable).</summary>
    public double IsolatedFractionOfLost => Lost > 0 ? (double)IsolatedLostFrames / Lost : 0;

    /// <summary>Mean gap length in frames (≈1 means isolated losses; larger means bursts).</summary>
    public double MeanGapFrames => GapCount > 0 ? (double)Lost / GapCount : 0;

    /// <summary>The longest single gap, in frames.</summary>
    public int MaxGapFrames
    {
        get
        {
            int max = 0;
            foreach (int len in BurstHistogram.Keys)
            {
                if (len > max)
                {
                    max = len;
                }
            }

            return max;
        }
    }

    /// <summary>
    /// Applies the decision rule from the investigation: FEC is only worth its cost (encoder bitrate +
    /// a ≥1-frame jitter buffer) when loss is both non-trivial and substantially isolated; bursty loss
    /// — the signature of TCP slow-consumer/reconnect drops — is recoverable only by PLC, not FEC.
    /// </summary>
    public string Verdict
    {
        get
        {
            if (Observed == 0)
            {
                return "NO DATA — received nothing. Check the url/subject/firewall and that the subscriber "
                    + "started before the publisher (core NATS has no replay).";
            }

            if (LossPercent < 0.05)
            {
                return $"NEGLIGIBLE LOSS ({Fmt(LossPercent)}%). Neither FEC nor PLC is justified on this "
                    + "path — a sequence number would buy observability only. Re-run under your worst "
                    + "realistic conditions (WiFi, saturation, a reconnect) before concluding.";
            }

            if (IsolatedFractionOfLost >= 0.5)
            {
                return $"ISOLATED-DOMINATED LOSS ({Fmt(LossPercent)}%; {Pct(IsolatedFractionOfLost)} of lost "
                    + "frames are single-frame gaps). Opus in-band FEC could recover most of these — but "
                    + "only with FEC enabled on the encoders (permanent bitrate cost) AND a ≥1-frame "
                    + "(20 ms) jitter buffer added to the receive path. Weigh that latency against the gain.";
            }

            return $"BURST-DOMINATED LOSS ({Fmt(LossPercent)}%; mean gap {Fmt(MeanGapFrames)} frames, max "
                + $"{MaxGapFrames}; only {Pct(IsolatedFractionOfLost)} isolated). Opus FEC recovers at most "
                + "the last frame of each burst, so it cannot help here. Add a sequence number for gap "
                + "detection + PLC concealment + observability; defer FEC.";
        }
    }

    /// <summary>Renders a human-readable report block.</summary>
    public string Render(bool sameClock)
    {
        var sb = new StringBuilder();
        sb.AppendLine("── Loss probe report ──────────────────────────────────");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Observed messages : {Observed}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Distinct received : {DistinctReceived}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Expected (span)   : {Expected}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Lost              : {Lost}  ({Fmt(LossPercent)}%)");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Gaps (bursts)     : {GapCount}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Isolated losses   : {IsolatedLostFrames}  ({Pct(IsolatedFractionOfLost)} of lost)");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Mean / max gap    : {Fmt(MeanGapFrames)} / {MaxGapFrames} frames");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Duplicates        : {Duplicates}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Reordered         : {Reordered}");

        if (BurstHistogram.Count > 0)
        {
            sb.AppendLine("  Gap-length histogram (frames : count):");
            foreach (KeyValuePair<int, int> entry in BurstHistogram)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"      {entry.Key,4} : {entry.Value}");
            }
        }

        sb.AppendLine(CultureInfo.InvariantCulture,
            $"  Inter-arrival ms  : mean {Fmt(Jitter.MeanMs)}  p50 {Fmt(Jitter.P50Ms)}  p95 {Fmt(Jitter.P95Ms)}  max {Fmt(Jitter.MaxMs)}");

        if (sameClock && Latency is { } latency)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"  One-way ms        : mean {Fmt(latency.MeanMs)}  p50 {Fmt(latency.P50Ms)}  p95 {Fmt(latency.P95Ms)}  max {Fmt(latency.MaxMs)}");
        }
        else
        {
            sb.AppendLine("  One-way ms        : (omitted — needs a shared clock; valid in local mode only)");
        }

        sb.AppendLine("───────────────────────────────────────────────────────");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  VERDICT: {Verdict}");
        return sb.ToString();
    }

    private static string Fmt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Pct(double fraction) => fraction.ToString("P0", CultureInfo.InvariantCulture);
}
