using System.Diagnostics;
using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Routing;
using Dasim.Radio.Messaging.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Dasim.Radio.MediaService.Tests.RoutingSample;

namespace Dasim.Radio.MediaService.Tests;

public sealed class MediaRouterServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MediaRouter Router(FloorHolders holders) =>
        new(new FakeForceTreeProvider(BuildRouting()), new FakeFloorHolders(holders), new PriorityOverrideMixPolicy());

    [Fact]
    public async Task Forwards_a_holders_frame_to_each_listener()
    {
        // p1 holds the group net A1a; the frame reaches the group leader and p2 (not p1 itself).
        FloorHolders holders = FloorHolders.From([new MixSource(Participant(P1), Net(A1a), new Priority(20))]);
        var payload = new byte[] { 1, 2, 3 };
        var bus = new ScriptedAudioBus([new AudioFrame(P1, payload)]);
        var service = new MediaRouterService(bus, Router(holders), NullLogger<MediaRouterService>.Instance);

        await service.StartAsync(Ct);
        try
        {
            await WaitForAsync(() => bus.Published.Count >= 2);
        }
        finally
        {
            await service.StopAsync(Ct);
        }

        (string ClientId, byte[] Opus)[] published = [.. bus.Published];
        string[] listeners = published.Select(p => p.ClientId).OrderBy(v => v, StringComparer.Ordinal).ToArray();
        Assert.Equal([A1a, P2], listeners);
        Assert.All(published, p => Assert.Equal(payload, p.Opus));
    }

    [Fact]
    public async Task A_frame_from_a_speaker_without_the_floor_publishes_nothing()
    {
        // The group leader holds the net; p2 (no floor) transmitting reaches nobody.
        FloorHolders holders = FloorHolders.From([new MixSource(Participant(A1a), Net(A1a), new Priority(40))]);
        var bus = new ScriptedAudioBus([new AudioFrame(P2, [9])]);
        var service = new MediaRouterService(bus, Router(holders), NullLogger<MediaRouterService>.Instance);

        await service.StartAsync(Ct);
        try
        {
            await Task.Delay(150, Ct); // give the frame time to be routed (to nobody)
        }
        finally
        {
            await service.StopAsync(Ct);
        }

        Assert.Empty(bus.Published);
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
