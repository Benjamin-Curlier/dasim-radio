using System.Globalization;
using System.Text.Json;
using Dasim.Radio.LossProbe;
using NATS.Client.Core;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // finish gracefully and print whatever we have
    cts.Cancel();
};

ProbeOptions? options = ProbeOptions.Parse(args, Console.Error);
if (options is null)
{
    PrintUsage();
    return args.Length == 0 ? 1 : 0;
}

if (!options.Validate(Console.Error))
{
    return 1;
}

try
{
    switch (options.Mode)
    {
        case ProbeMode.Publish:
            await using (var connection = new NatsConnection(NatsOptsFactory.ForPublisher(options.Url)))
            {
                await Publisher.RunAsync(connection, options, cts.Token).ConfigureAwait(false);
            }

            return 0;

        case ProbeMode.Subscribe:
            await using (var connection = new NatsConnection(NatsOptsFactory.ForSubscriber(options)))
            {
                LossReport report = await Subscriber.RunAsync(connection, options, cts.Token).ConfigureAwait(false);
                Emit(report, options);
            }

            return 0;

        case ProbeMode.Both:
            await using (var subConnection = new NatsConnection(NatsOptsFactory.ForSubscriber(options)))
            await using (var pubConnection = new NatsConnection(NatsOptsFactory.ForPublisher(options.Url)))
            {
                LossReport report = await ProbeSession
                    .RunAsync(subConnection, pubConnection, options, cts.Token).ConfigureAwait(false);
                Emit(report, options);
            }

            return 0;

        case ProbeMode.Local:
            LossReport localReport = await LocalRunner.RunAsync(options, cts.Token).ConfigureAwait(false);
            Emit(localReport, options);
            return 0;

        default:
            return 1;
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Cancelled.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Probe failed: {ex.Message}");
    if (options.Mode == ProbeMode.Local)
    {
        Console.Error.WriteLine(
            "Local mode needs a reachable Docker daemon (Linux containers). "
            + "Alternatively run `docker run -p 4222:4222 nats:2.10` and use the pub/sub modes with --url.");
    }

    return 2;
}

void Emit(LossReport report, ProbeOptions opts)
{
    Console.WriteLine();
    Console.WriteLine(report.Render(opts.SameClock));

    if (opts.JsonPath is { } path)
    {
        WriteJson(report, opts, path);
        Console.WriteLine($"Wrote JSON report to {path}.");
    }
}

static void WriteJson(LossReport report, ProbeOptions opts, string path)
{
    var payload = new
    {
        opts.Mode,
        opts.Subject,
        opts.RateHz,
        opts.PayloadBytes,
        report.Observed,
        report.DistinctReceived,
        report.Expected,
        report.Lost,
        report.LossPercent,
        report.IsolatedLostFrames,
        report.IsolatedFractionOfLost,
        report.GapCount,
        report.MeanGapFrames,
        report.MaxGapFrames,
        report.Duplicates,
        report.Reordered,
        BurstHistogram = report.BurstHistogram.ToDictionary(
            kv => kv.Key.ToString(CultureInfo.InvariantCulture), kv => kv.Value),
        report.Jitter,
        report.Latency,
        report.Verdict,
    };

    File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        Dasim.Radio loss probe — measures data-plane (audio.out) loss and its SHAPE, so you can
        decide whether Opus FEC/PLC is worth adding. The deciding number is the isolated-loss
        fraction: in-band FEC can only recover single-frame gaps, not the bursts that TCP
        slow-consumer / reconnect drops produce.

        USAGE:
          loss-probe local [options]     Spin a NATS container (Docker) and run pub+sub in-process.
          loss-probe both  [options]     Run pub+sub in-process against an external --url (the netem rig).
          loss-probe sub   [options]     Subscribe and analyse (run this first on the listener box).
          loss-probe pub   [options]     Publish a paced stream (run on the speaker box).

        OPTIONS (defaults in brackets):
          --url <nats://host:4222>       NATS URL                                [nats://127.0.0.1:4222]
          --subject <name>              Subject to publish/subscribe             [probe.audio.out]
          --rate <fps>                  Frames per second (radio runs at 50)     [50]
          --payload <bytes>             Frame size (24 kbps/20 ms ≈ 60)          [60]
          --duration <seconds>          Publish duration                         [30]
          --count <frames>              Exact frame count (overrides duration)   [0 = use duration]
          --consumer-delay <ms>         Slow the subscriber to induce drops      [0]
          --sub-capacity <n>            Subscriber pending-channel capacity      [1024]
          --sub-fullmode <mode>         wait | dropnewest | dropoldest           [wait]
          --idle-timeout <ms>           Stop this long after the last frame      [2000]
          --startup-timeout <ms>        Give up if no first frame by then        [30000]
          --json <path>                 Also write the report as JSON            [none]

        EXAMPLES:
          loss-probe local --duration 20
          loss-probe local --consumer-delay 40 --sub-capacity 16 --sub-fullmode dropnewest
          # emulated impaired LAN (see compose/ — start the rig first):
          loss-probe both --url nats://127.0.0.1:4222 --duration 30
          # two machines:
          loss-probe sub --url nats://10.0.0.5:4222
          loss-probe pub --url nats://10.0.0.5:4222 --duration 60
        """);
}
