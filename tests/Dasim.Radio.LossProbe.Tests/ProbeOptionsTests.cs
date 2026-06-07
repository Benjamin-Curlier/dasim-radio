using System.Threading.Channels;
using Dasim.Radio.LossProbe;
using Xunit;

namespace Dasim.Radio.LossProbe.Tests;

public sealed class ProbeOptionsTests
{
    [Theory]
    [InlineData("local", ProbeMode.Local, true)]
    [InlineData("both", ProbeMode.Both, true)]
    [InlineData("pub", ProbeMode.Publish, false)]
    [InlineData("publish", ProbeMode.Publish, false)]
    [InlineData("sub", ProbeMode.Subscribe, false)]
    [InlineData("subscribe", ProbeMode.Subscribe, false)]
    public void Parses_modes_and_sets_same_clock_for_single_process_modes(string verb, ProbeMode mode, bool sameClock)
    {
        ProbeOptions? options = ProbeOptions.Parse([verb], new StringWriter());

        Assert.NotNull(options);
        Assert.Equal(mode, options.Mode);
        Assert.Equal(sameClock, options.SameClock);
    }

    [Fact]
    public void Parses_flags()
    {
        ProbeOptions? options = ProbeOptions.Parse(
            ["sub", "--url", "nats://10.0.0.5:4222", "--subject", "x.y", "--rate", "100", "--payload", "120",
             "--consumer-delay", "40", "--sub-capacity", "16", "--sub-fullmode", "dropnewest", "--json", "out.json"],
            new StringWriter());

        Assert.NotNull(options);
        Assert.Equal("nats://10.0.0.5:4222", options.Url);
        Assert.Equal("x.y", options.Subject);
        Assert.Equal(100, options.RateHz);
        Assert.Equal(120, options.PayloadBytes);
        Assert.Equal(40, options.ConsumerDelayMs);
        Assert.Equal(16, options.SubCapacity);
        Assert.Equal(BoundedChannelFullMode.DropNewest, options.SubFullMode);
        Assert.Equal("out.json", options.JsonPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Returns_null_for_no_args_or_help(string arg)
    {
        string[] args = arg.Length == 0 ? [] : [arg];

        Assert.Null(ProbeOptions.Parse(args, new StringWriter()));
    }

    [Fact]
    public void Rejects_unknown_mode_and_explains()
    {
        var error = new StringWriter();

        Assert.Null(ProbeOptions.Parse(["wat"], error));
        Assert.Contains("Unknown mode", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_flag_with_a_missing_value()
    {
        var error = new StringWriter();

        Assert.Null(ProbeOptions.Parse(["sub", "--rate"], error));
        Assert.Contains("Missing value", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_non_numeric_value()
    {
        var error = new StringWriter();

        Assert.Null(ProbeOptions.Parse(["pub", "--rate", "fast"], error));
        Assert.Contains("integer", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--rate", "0", "rate")]
    [InlineData("--payload", "4", "payload")]
    public void Validate_rejects_out_of_range_values(string flag, string value, string expectedWord)
    {
        ProbeOptions? options = ProbeOptions.Parse(["pub", flag, value], new StringWriter());
        Assert.NotNull(options);

        var error = new StringWriter();
        Assert.False(options.Validate(error));
        Assert.Contains(expectedWord, error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_accepts_a_sensible_default_run()
    {
        ProbeOptions? options = ProbeOptions.Parse(["both"], new StringWriter());
        Assert.NotNull(options);
        Assert.True(options.Validate(new StringWriter()));
    }
}
