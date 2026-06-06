using System.Diagnostics;
using Dasim.Radio.Contracts;
using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Degrade;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dasim.Radio.MediaService.Tests;

public sealed class DegradeCommandServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Applies_scripted_commands_to_the_registry()
    {
        var registry = new DegradeRegistry();
        var channel = new ScriptedDegradeChannel([new DegradeCommand("L1", NetId: null, 30, 20)]);
        var service = new DegradeCommandService(channel, registry, NullLogger<DegradeCommandService>.Instance);

        await service.StartAsync(Ct);
        try
        {
            await WaitForAsync(() => registry.TryGetProfile(new ParticipantId("L1"), out _));
        }
        finally
        {
            await service.StopAsync(Ct);
        }

        Assert.True(registry.TryGetProfile(new ParticipantId("L1"), out DegradeProfile profile));
        Assert.Equal(30, profile.QualityPercent);
        Assert.Equal(20, profile.ClarityPercent);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(20, Ct);
        }

        Assert.True(condition(), "Condition was not met within the timeout.");
    }
}
