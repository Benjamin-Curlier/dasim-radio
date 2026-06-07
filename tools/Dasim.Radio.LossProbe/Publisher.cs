using System.Diagnostics;
using NATS.Client.Core;

namespace Dasim.Radio.LossProbe;

/// <summary>
/// Publishes a paced stream of sequence-stamped probe frames to a subject — the producer side of the
/// measurement. The cadence is driven off a <see cref="Stopwatch"/> deadline (not a coarse timer) so the
/// average rate is faithful to the requested fps, and the payload buffer is reused across frames exactly
/// as the production transmit pump does (NATS copies into its write pipe before the publish completes).
/// </summary>
public static class Publisher
{
    public static async Task<long> RunAsync(INatsConnection connection, ProbeOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);

        byte[] frame = new byte[Math.Max(options.PayloadBytes, ProbeFrame.HeaderBytes)];
        frame.AsSpan(ProbeFrame.HeaderBytes).Fill(ProbeFrame.FillerByte);

        long total = options.Count > 0
            ? options.Count
            : (long)Math.Round(options.RateHz * options.DurationSeconds);

        double ticksPerFrame = (double)Stopwatch.Frequency / options.RateHz;
        long start = Stopwatch.GetTimestamp();

        Console.WriteLine(
            $"Publishing {total} frames of {frame.Length} B at {options.RateHz} fps to '{options.Subject}'…");

        long sent = 0;
        long failed = 0;
        for (long seq = 0; seq < total; seq++)
        {
            await PaceAsync(start, (long)(ticksPerFrame * seq), cancellationToken).ConfigureAwait(false);

            ProbeFrame.WriteHeader(frame, (uint)seq, Stopwatch.GetTimestamp());
            try
            {
                await connection.PublishAsync(options.Subject, frame, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A transient publish failure (e.g. mid-reconnect) must not abort the run — that's exactly
                // the scenario we measure. Keep pacing; the sequence keeps advancing, so the subscriber
                // sees the outage as a gap.
                failed++;
            }
        }

        string suffix = failed > 0 ? $" ({failed} publish failures during the run)" : string.Empty;
        Console.WriteLine($"Published {sent} frames{suffix}.");
        return sent;
    }

    // Waits until the deadline (start + offsetTicks): sleep off the bulk, spin the final sub-millisecond
    // so the average cadence stays accurate without a busy loop burning a core for the whole run.
    private static async Task PaceAsync(long start, long offsetTicks, CancellationToken cancellationToken)
    {
        long due = start + offsetTicks;
        while (true)
        {
            long remaining = due - Stopwatch.GetTimestamp();
            if (remaining <= 0)
            {
                return;
            }

            int remainingMs = (int)(remaining * 1000 / Stopwatch.Frequency);
            if (remainingMs > 2)
            {
                await Task.Delay(remainingMs - 1, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                Thread.SpinWait(100);
            }
        }
    }
}
