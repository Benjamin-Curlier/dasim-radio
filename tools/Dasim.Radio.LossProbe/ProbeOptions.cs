using System.Globalization;
using System.Threading.Channels;

namespace Dasim.Radio.LossProbe;

/// <summary>What the process should do.</summary>
public enum ProbeMode
{
    /// <summary>Spin a throwaway NATS server (Docker) and run publisher + subscriber in-process.</summary>
    Local,

    /// <summary>Publish a paced stream of probe frames to a subject.</summary>
    Publish,

    /// <summary>Subscribe to a subject and analyse the stream for loss.</summary>
    Subscribe,
}

/// <summary>Parsed command-line options for one probe run.</summary>
public sealed record ProbeOptions
{
    public ProbeMode Mode { get; init; }

    public string Url { get; init; } = "nats://127.0.0.1:4222";

    public string Subject { get; init; } = "probe.audio.out";

    /// <summary>Frames per second to publish (the radio runs at 50).</summary>
    public int RateHz { get; init; } = 50;

    /// <summary>Total frame size in bytes (a 24 kbps / 20 ms Opus voice frame is ≈60).</summary>
    public int PayloadBytes { get; init; } = 60;

    /// <summary>How long to publish, in seconds (ignored when <see cref="Count"/> &gt; 0).</summary>
    public double DurationSeconds { get; init; } = 30;

    /// <summary>Exact number of frames to publish; 0 means use <see cref="DurationSeconds"/>.</summary>
    public long Count { get; init; }

    /// <summary>Artificial per-frame delay on the subscriber, to induce slow-consumer drops (ms).</summary>
    public int ConsumerDelayMs { get; init; }

    /// <summary>Subscriber pending-channel capacity; a small value forces slow-consumer behaviour sooner.</summary>
    public int SubCapacity { get; init; } = 1024;

    /// <summary>What the subscriber's pending channel does when full (Wait mirrors production).</summary>
    public BoundedChannelFullMode SubFullMode { get; init; } = BoundedChannelFullMode.Wait;

    /// <summary>Stop the subscriber this long after the last received frame (ms).</summary>
    public int IdleTimeoutMs { get; init; } = 2000;

    /// <summary>Give up if no first frame arrives within this long (ms).</summary>
    public int StartupTimeoutMs { get; init; } = 30_000;

    /// <summary>Optional path to also write the report as JSON.</summary>
    public string? JsonPath { get; init; }

    /// <summary>True when publisher and subscriber share a clock, so one-way latency is meaningful.</summary>
    public bool SameClock { get; init; }

    /// <summary>
    /// Parses argv into options, or returns null and writes an error/usage when the input is invalid or
    /// help was requested.
    /// </summary>
    public static ProbeOptions? Parse(string[] args, TextWriter error)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            return null;
        }

        ProbeMode mode;
        switch (args[0].ToLowerInvariant())
        {
            case "local":
                mode = ProbeMode.Local;
                break;
            case "pub":
            case "publish":
                mode = ProbeMode.Publish;
                break;
            case "sub":
            case "subscribe":
                mode = ProbeMode.Subscribe;
                break;
            default:
                error.WriteLine($"Unknown mode '{args[0]}'. Expected: local | pub | sub.");
                return null;
        }

        var options = new ProbeOptions { Mode = mode, SameClock = mode == ProbeMode.Local };

        for (int i = 1; i < args.Length; i++)
        {
            string flag = args[i];
            if (IsHelp(flag))
            {
                return null;
            }

            if (!flag.StartsWith("--", StringComparison.Ordinal))
            {
                error.WriteLine($"Unexpected argument '{flag}'.");
                return null;
            }

            if (!TryNextValue(args, ref i, out string value))
            {
                error.WriteLine($"Missing value for '{flag}'.");
                return null;
            }

            if (!Apply(ref options, flag, value, error))
            {
                return null;
            }
        }

        return options;
    }

    private static bool Apply(ref ProbeOptions options, string flag, string value, TextWriter error)
    {
        switch (flag)
        {
            case "--url":
                options = options with { Url = value };
                return true;
            case "--subject":
                options = options with { Subject = value };
                return true;
            case "--rate":
                return TryInt(value, flag, error, out int rate) && Set(ref options, o => o with { RateHz = rate });
            case "--payload":
                return TryInt(value, flag, error, out int payload) && Set(ref options, o => o with { PayloadBytes = payload });
            case "--duration":
                return TryDouble(value, flag, error, out double dur) && Set(ref options, o => o with { DurationSeconds = dur });
            case "--count":
                return TryLong(value, flag, error, out long count) && Set(ref options, o => o with { Count = count });
            case "--consumer-delay":
                return TryInt(value, flag, error, out int delay) && Set(ref options, o => o with { ConsumerDelayMs = delay });
            case "--sub-capacity":
                return TryInt(value, flag, error, out int cap) && Set(ref options, o => o with { SubCapacity = cap });
            case "--sub-fullmode":
                return TryFullMode(value, error, out BoundedChannelFullMode m) && Set(ref options, o => o with { SubFullMode = m });
            case "--idle-timeout":
                return TryInt(value, flag, error, out int idle) && Set(ref options, o => o with { IdleTimeoutMs = idle });
            case "--startup-timeout":
                return TryInt(value, flag, error, out int startup) && Set(ref options, o => o with { StartupTimeoutMs = startup });
            case "--json":
                options = options with { JsonPath = value };
                return true;
            default:
                error.WriteLine($"Unknown flag '{flag}'.");
                return false;
        }
    }

    /// <summary>Validates cross-field constraints, writing the first problem to <paramref name="error"/>.</summary>
    public bool Validate(TextWriter error)
    {
        if (RateHz <= 0)
        {
            error.WriteLine("--rate must be positive.");
            return false;
        }

        if (PayloadBytes < ProbeFrame.HeaderBytes)
        {
            error.WriteLine($"--payload must be at least {ProbeFrame.HeaderBytes} (the probe header size).");
            return false;
        }

        if (Count <= 0 && DurationSeconds <= 0)
        {
            error.WriteLine("Set a positive --duration or --count.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(Subject))
        {
            error.WriteLine("--subject must not be empty.");
            return false;
        }

        return true;
    }

    private static bool Set(ref ProbeOptions options, Func<ProbeOptions, ProbeOptions> update)
    {
        options = update(options);
        return true;
    }

    private static bool TryNextValue(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            return false;
        }

        value = args[++index];
        return true;
    }

    private static bool TryInt(string value, string flag, TextWriter error, out int result)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        error.WriteLine($"'{flag}' expects an integer, got '{value}'.");
        return false;
    }

    private static bool TryLong(string value, string flag, TextWriter error, out long result)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        error.WriteLine($"'{flag}' expects an integer, got '{value}'.");
        return false;
    }

    private static bool TryDouble(string value, string flag, TextWriter error, out double result)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        error.WriteLine($"'{flag}' expects a number, got '{value}'.");
        return false;
    }

    private static bool TryFullMode(string value, TextWriter error, out BoundedChannelFullMode mode)
    {
        switch (value.ToLowerInvariant())
        {
            case "wait":
                mode = BoundedChannelFullMode.Wait;
                return true;
            case "dropnewest":
                mode = BoundedChannelFullMode.DropNewest;
                return true;
            case "dropoldest":
                mode = BoundedChannelFullMode.DropOldest;
                return true;
            default:
                error.WriteLine($"--sub-fullmode expects wait | dropnewest | dropoldest, got '{value}'.");
                mode = BoundedChannelFullMode.Wait;
                return false;
        }
    }

    private static bool IsHelp(string arg) =>
        arg is "--help" or "-h" or "/?" or "help";
}
