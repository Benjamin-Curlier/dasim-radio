using NATS.Client.Core;
using Testcontainers.Nats;

namespace Dasim.Radio.LossProbe;

/// <summary>
/// Zero-setup baseline: starts a throwaway NATS server in Docker (same image the integration tests use),
/// then runs the subscriber and publisher in-process against it. Because both halves share one clock the
/// one-way latency figures are valid here, unlike a split LAN run. Use this for a healthy-path baseline
/// and to reproduce slow-consumer drops deterministically with <c>--consumer-delay</c>.
/// </summary>
public static class LocalRunner
{
    private const string NatsImage = "nats:2.10";

    public static async Task<LossReport> RunAsync(ProbeOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        Console.WriteLine($"Starting throwaway NATS container ({NatsImage})… (needs Docker)");
        await using var container = new NatsBuilder(NatsImage).Build();
        await container.StartAsync(cancellationToken).ConfigureAwait(false);

        string url = container.GetConnectionString();
        Console.WriteLine($"NATS up at {url}.");

        ProbeOptions runOptions = options with { Url = url };

        await using var subConnection = new NatsConnection(NatsOptsFactory.ForSubscriber(runOptions));
        await using var pubConnection = new NatsConnection(NatsOptsFactory.ForPublisher(url));

        // Start the subscriber first and let the subscription establish — core NATS has no replay, so
        // anything published before the interest is registered is simply never delivered.
        Task<LossReport> subscriberTask = Subscriber.RunAsync(subConnection, runOptions, cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);

        await Publisher.RunAsync(pubConnection, runOptions, cancellationToken).ConfigureAwait(false);

        return await subscriberTask.ConfigureAwait(false);
    }
}
