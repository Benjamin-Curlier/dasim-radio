using System.Diagnostics;
using Dasim.Radio.Contracts;
using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Floor;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dasim.Radio.MediaService.Tests;

public sealed class FloorAuthorityServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pumps_a_scripted_request_stream_through_the_arbiter_in_order()
    {
        // Two requests on one stream: a grant then a higher-priority pre-emption. A single stream
        // keeps the channel order deterministic (cross-stream request/release ordering is racy by
        // design and not asserted here).
        var requests = new[]
        {
            new FloorRequestMessage("alpha", "low", 1),
            new FloorRequestMessage("alpha", "high", 9),
        };
        var signal = new ScriptedFloorSignal(requests, []);

        var floor = new FloorControlService(new FakeTimeProvider(Now));
        var arbiter = new FloorArbiter(
            floor,
            new RequestPriorityResolver(NullLogger<RequestPriorityResolver>.Instance),
            signal,
            new RecordingFloorStateWriter(),
            NullLogger<FloorArbiter>.Instance);
        var service = new FloorAuthorityService(signal, arbiter, NullLogger<FloorAuthorityService>.Instance);

        await service.StartAsync(Ct);
        try
        {
            await WaitForAsync(() => signal.Published.Count >= 2);
        }
        finally
        {
            await service.StopAsync(Ct);
        }

        FloorEventMessage[] events = [.. signal.Published];
        Assert.Equal(FloorOutcomes.Granted, events[0].Outcome);
        Assert.Equal("low", events[0].Requester);
        Assert.Equal(FloorOutcomes.GrantedWithPreemption, events[1].Outcome);
        Assert.Equal("high", events[1].Requester);
        Assert.Equal("low", events[1].Preempted);
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
