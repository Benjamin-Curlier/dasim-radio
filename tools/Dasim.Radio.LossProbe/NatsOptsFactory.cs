using NATS.Client.Core;

namespace Dasim.Radio.LossProbe;

/// <summary>Builds <see cref="NatsOpts"/> for the probe roles, including the slow-consumer knobs.</summary>
public static class NatsOptsFactory
{
    /// <summary>Publisher options: defaults, just the URL and a recognisable name.</summary>
    public static NatsOpts ForPublisher(string url) =>
        NatsOpts.Default with { Url = url, Name = "loss-probe-pub" };

    /// <summary>
    /// Subscriber options with the pending-channel capacity / full-mode wired from the probe options, so
    /// a small capacity + a slow consumer reproduces the slow-consumer drop behaviour deterministically.
    /// </summary>
    public static NatsOpts ForSubscriber(ProbeOptions options) =>
        NatsOpts.Default with
        {
            Url = options.Url,
            Name = "loss-probe-sub",
            SubPendingChannelCapacity = options.SubCapacity,
            SubPendingChannelFullMode = options.SubFullMode,
        };
}
