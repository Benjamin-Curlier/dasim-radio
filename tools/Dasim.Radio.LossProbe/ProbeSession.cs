using NATS.Client.Core;

namespace Dasim.Radio.LossProbe;

/// <summary>
/// Runs a publisher and subscriber together against an already-connected pair, returning the
/// subscriber's report. Shared by <see cref="LocalRunner"/> (its own container) and the <c>both</c> mode
/// (an external NATS — e.g. the Dockerised <c>nats</c> + netem shaper). Both halves share one clock, so
/// the one-way latency figures are valid.
/// </summary>
public static class ProbeSession
{
    public static async Task<LossReport> RunAsync(
        INatsConnection subConnection,
        INatsConnection pubConnection,
        ProbeOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subConnection);
        ArgumentNullException.ThrowIfNull(pubConnection);
        ArgumentNullException.ThrowIfNull(options);

        // Start the subscriber first and let the subscription establish — core NATS has no replay, so
        // anything published before the interest is registered is simply never delivered.
        Task<LossReport> subscriberTask = Subscriber.RunAsync(subConnection, options, cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);

        await Publisher.RunAsync(pubConnection, options, cancellationToken).ConfigureAwait(false);

        return await subscriberTask.ConfigureAwait(false);
    }
}
