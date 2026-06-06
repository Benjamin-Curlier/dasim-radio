using Dasim.Radio.Contracts;
using Dasim.Radio.Manager.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dasim.Radio.Manager.Core.Tests;

public sealed class DegradeControlServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (DegradeControlService Service, FakeDegradeChannel Channel) Build()
    {
        var channel = new FakeDegradeChannel();
        return (new DegradeControlService(channel, NullLogger<DegradeControlService>.Instance), channel);
    }

    [Fact]
    public async Task Degrade_publishes_the_command_with_net_scope()
    {
        (DegradeControlService service, FakeDegradeChannel channel) = Build();

        await service.DegradeAsync("listener-1", 60, 40, netId: "alpha", Ct);

        DegradeCommand command = Assert.Single(channel.Published);
        Assert.Equal("listener-1", command.TargetClientId);
        Assert.Equal("alpha", command.NetId);
        Assert.Equal(60, command.QualityPercent);
        Assert.Equal(40, command.ClarityPercent);
    }

    [Fact]
    public async Task Degrade_clamps_out_of_range_values()
    {
        (DegradeControlService service, FakeDegradeChannel channel) = Build();

        await service.DegradeAsync("listener-1", qualityPercent: 150, clarityPercent: -20, cancellationToken: Ct);

        DegradeCommand command = Assert.Single(channel.Published);
        Assert.Equal(100, command.QualityPercent);
        Assert.Equal(0, command.ClarityPercent);
    }

    [Fact]
    public async Task Reset_restores_full_quality_and_clarity()
    {
        (DegradeControlService service, FakeDegradeChannel channel) = Build();

        await service.ResetAsync("listener-1", Ct);

        DegradeCommand command = Assert.Single(channel.Published);
        Assert.Equal(100, command.QualityPercent);
        Assert.Equal(100, command.ClarityPercent);
        Assert.Null(command.NetId);
    }

    [Theory]
    [InlineData("bad.target", null)]
    [InlineData("listener-1", "bad>net")]
    public async Task Degrade_rejects_non_token_targets(string target, string? net)
    {
        (DegradeControlService service, FakeDegradeChannel channel) = Build();

        await Assert.ThrowsAsync<ArgumentException>(() => service.DegradeAsync(target, 50, 50, net, Ct).AsTask());
        Assert.Empty(channel.Published);
    }
}
