using System.Diagnostics;
using NATS.Client.Core;

namespace Dasim.Radio.LossProbe;

/// <summary>
/// Subscribes to the probe subject, feeds every frame to a <see cref="LossAnalyzer"/>, and returns the
/// report once the stream goes idle. Counts NATS disconnects — on core NATS each one is an unrecoverable
/// burst gap (no replay), which is exactly the kind of loss Opus FEC cannot fix, so it belongs in the
/// picture. An optional per-frame delay and a small pending-channel capacity let you reproduce the
/// slow-consumer drop mode on demand.
/// </summary>
public static class Subscriber
{
    public static async Task<LossReport> RunAsync(INatsConnection connection, ProbeOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);

        var analyzer = new LossAnalyzer();
        int disconnects = 0;

        NatsConnection? concrete = connection as NatsConnection;
        ValueTask OnDisconnected(object? sender, NatsEventArgs args)
        {
            Interlocked.Increment(ref disconnects);
            return ValueTask.CompletedTask;
        }

        if (concrete is not null)
        {
            concrete.ConnectionDisconnected += OnDisconnected;
        }

        Console.WriteLine(
            $"Subscribed to '{options.Subject}'. Waiting for frames (Ctrl+C to stop)…");

        try
        {
            await ConsumeAsync(connection, options, analyzer, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C (or shutdown) — stop consuming and report what we have so far.
        }
        finally
        {
            if (concrete is not null)
            {
                concrete.ConnectionDisconnected -= OnDisconnected;
            }
        }

        if (analyzer.Observed == 0)
        {
            Console.WriteLine("No frames arrived (startup timeout or cancelled before the first frame).");
        }

        LossReport report = analyzer.Build(options.SameClock);
        if (disconnects > 0)
        {
            Console.WriteLine($"NATS disconnects during the run: {disconnects} (each is a burst gap on core NATS).");
        }

        return report;
    }

    private static async Task ConsumeAsync(
        INatsConnection connection, ProbeOptions options, LossAnalyzer analyzer, CancellationToken cancellationToken)
    {
        // The run ends when the stream goes quiet: an idle CTS cancels the subscription after the startup
        // window (before the first frame) or the idle timeout (after it), reset on every frame. This is
        // the safe way to bound an `await foreach` — abandoning a pending MoveNextAsync and then disposing
        // the enumerator throws. Cancellation (idle or Ctrl+C) surfaces as OperationCanceledException,
        // which the caller treats as a clean stop.
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idle.CancelAfter(options.StartupTimeoutMs);

        await foreach (NatsMsg<byte[]> msg in
            connection.SubscribeAsync<byte[]>(options.Subject, cancellationToken: idle.Token).ConfigureAwait(false))
        {
            idle.CancelAfter(options.IdleTimeoutMs);

            if (options.ConsumerDelayMs > 0)
            {
                // Deliberately slow: backs up the pending channel to provoke slow-consumer drops. Uses the
                // outer token so a real Ctrl+C still interrupts the delay.
                await Task.Delay(options.ConsumerDelayMs, cancellationToken).ConfigureAwait(false);
            }

            if (msg.Data is { } data && ProbeFrame.TryReadHeader(data, out uint seq, out long sendTicks))
            {
                analyzer.Observe(seq, Stopwatch.GetTimestamp(), sendTicks);
            }
        }
    }
}
