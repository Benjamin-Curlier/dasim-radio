using Dasim.Radio.Contracts;
using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Floor;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dasim.Radio.MediaService.Tests;

public sealed class FloorArbiterTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(FloorArbiter Arbiter, RecordingFloorSignal Signal, RecordingFloorStateWriter Writer);

    private static Harness Build(IFloorPriorityResolver? resolver = null)
    {
        var floor = new FloorControlService(new FakeTimeProvider(Now));
        var signal = new RecordingFloorSignal();
        var writer = new RecordingFloorStateWriter();
        var arbiter = new FloorArbiter(
            floor,
            resolver ?? new RequestPriorityResolver(NullLogger<RequestPriorityResolver>.Instance),
            signal,
            writer,
            NullLogger<FloorArbiter>.Instance);
        return new Harness(arbiter, signal, writer);
    }

    private static FloorRequestMessage Request(string net, string participant, int priority) =>
        new(net, participant, priority);

    [Fact]
    public async Task Request_on_idle_net_is_granted_and_persisted()
    {
        Harness h = Build();

        await h.Arbiter.HandleRequestAsync(Request("alpha", "p1", 5), Ct);

        FloorEventMessage evt = Assert.Single(h.Signal.Published);
        Assert.Equal("alpha", evt.NetId);
        Assert.Equal(FloorOutcomes.Granted, evt.Outcome);
        Assert.Equal("p1", evt.Requester);
        Assert.Null(evt.Preempted);
        Assert.Equal("p1", evt.CurrentHolder);

        FloorStateDto state = Assert.Single(h.Writer.Written);
        Assert.Equal("alpha", state.NetId);
        Assert.Equal("p1", state.HolderParticipantId);
        Assert.Equal(5, state.HolderPriority);
        Assert.Equal(Now, state.HeldSinceUtc);
    }

    [Fact]
    public async Task Higher_priority_request_preempts_and_reports_the_preempted_holder()
    {
        Harness h = Build();
        await h.Arbiter.HandleRequestAsync(Request("alpha", "low", 1), Ct);

        await h.Arbiter.HandleRequestAsync(Request("alpha", "high", 9), Ct);

        FloorEventMessage evt = h.Signal.Published[^1];
        Assert.Equal(FloorOutcomes.GrantedWithPreemption, evt.Outcome);
        Assert.Equal("high", evt.Requester);
        Assert.Equal("low", evt.Preempted);
        Assert.Equal("high", evt.CurrentHolder);

        FloorStateDto state = h.Writer.Written[^1];
        Assert.Equal("high", state.HolderParticipantId);
        Assert.Equal(9, state.HolderPriority);
    }

    [Theory]
    [InlineData(5)] // equal priority
    [InlineData(1)] // lower priority
    public async Task Equal_or_lower_priority_request_is_denied_without_persisting(int challengerPriority)
    {
        Harness h = Build();
        await h.Arbiter.HandleRequestAsync(Request("alpha", "holder", 5), Ct);
        int writesAfterGrant = h.Writer.Written.Count;

        await h.Arbiter.HandleRequestAsync(Request("alpha", "challenger", challengerPriority), Ct);

        FloorEventMessage evt = h.Signal.Published[^1];
        Assert.Equal(FloorOutcomes.Denied, evt.Outcome);
        Assert.Equal("challenger", evt.Requester);
        Assert.Null(evt.Preempted);
        Assert.Equal("holder", evt.CurrentHolder); // the denied requester still learns who holds
        Assert.Equal(writesAfterGrant, h.Writer.Written.Count); // denial changes nothing
    }

    [Fact]
    public async Task Release_by_holder_frees_the_net_and_persists_idle()
    {
        Harness h = Build();
        await h.Arbiter.HandleRequestAsync(Request("alpha", "p1", 5), Ct);

        await h.Arbiter.HandleReleaseAsync(new FloorReleaseMessage("alpha", "p1"), Ct);

        FloorEventMessage evt = h.Signal.Published[^1];
        Assert.Equal(FloorOutcomes.Released, evt.Outcome);
        Assert.Equal("p1", evt.Requester);
        Assert.Null(evt.CurrentHolder); // net is now idle

        FloorStateDto state = h.Writer.Written[^1];
        Assert.Null(state.HolderParticipantId);
        Assert.Null(state.HolderPriority);
        Assert.Null(state.HeldSinceUtc);
    }

    [Fact]
    public async Task Release_by_non_holder_is_ignored()
    {
        Harness h = Build();
        await h.Arbiter.HandleRequestAsync(Request("alpha", "holder", 5), Ct);
        int published = h.Signal.Published.Count;
        int written = h.Writer.Written.Count;

        await h.Arbiter.HandleReleaseAsync(new FloorReleaseMessage("alpha", "someone-else"), Ct);

        Assert.Equal(published, h.Signal.Published.Count);
        Assert.Equal(written, h.Writer.Written.Count);
    }

    [Fact]
    public async Task Persistence_failure_does_not_fail_the_decision_or_suppress_the_event()
    {
        // floor_state is observability; a KV write failure must not stop the authoritative event.
        var floor = new FloorControlService(new FakeTimeProvider(Now));
        var signal = new RecordingFloorSignal();
        var arbiter = new FloorArbiter(
            floor,
            new RequestPriorityResolver(NullLogger<RequestPriorityResolver>.Instance),
            signal,
            new ThrowingFloorStateWriter(),
            NullLogger<FloorArbiter>.Instance);

        await arbiter.HandleRequestAsync(Request("alpha", "p1", 5), Ct);

        FloorEventMessage evt = Assert.Single(signal.Published);
        Assert.Equal(FloorOutcomes.Granted, evt.Outcome);
        Assert.Equal("p1", evt.CurrentHolder);
    }

    [Fact]
    public async Task Arbiter_arbitrates_on_the_resolved_priority_not_the_wire_priority()
    {
        // Resolver inverts the wire priorities: by the wire, "holder" (50) would out-rank
        // "elevated" (1) and deny it; by the resolved ranks, "elevated" (20) pre-empts "holder" (10).
        Harness h = Build(new MappingPriorityResolver(new() { ["holder"] = 10, ["elevated"] = 20 }));
        await h.Arbiter.HandleRequestAsync(Request("alpha", "holder", 50), Ct);

        await h.Arbiter.HandleRequestAsync(Request("alpha", "elevated", 1), Ct);

        FloorEventMessage evt = h.Signal.Published[^1];
        Assert.Equal(FloorOutcomes.GrantedWithPreemption, evt.Outcome);
        Assert.Equal("elevated", evt.Requester);
        Assert.Equal("holder", evt.Preempted);
        Assert.Equal("elevated", evt.CurrentHolder);
    }
}
