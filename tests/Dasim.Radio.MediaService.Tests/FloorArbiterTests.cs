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

    // Net "alpha" with every participant the arbiter tests use as a member, so the force-tree membership
    // gate admits them; arbitration priority still comes from the wire via the resolver under test.
    private static readonly FakeForceTreeProvider Force = new(
        RoutingSample.SingleNet("alpha", "p1", "low", "high", "holder", "challenger", "elevated"));

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
            Force,
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

    [Theory]
    [InlineData("alpha", "outsider")] // unknown participant (not in the force tree)
    [InlineData("bravo", "p1")]       // p1 is a member of 'alpha', not 'bravo'
    [InlineData(">", "p1")]            // forged/wildcard NetId — a subject-injection attempt
    [InlineData("a.b", "p1")]          // dotted NetId — a KV-key / multi-token injection attempt
    public async Task A_request_for_a_net_the_participant_is_not_a_member_of_is_dropped(string net, string participant)
    {
        Harness h = Build();

        await h.Arbiter.HandleRequestAsync(Request(net, participant, 5), Ct);

        // No grant, so nothing is broadcast (no floor.events.<net> publish — closing the subject injection)
        // and nothing is persisted (no floor_state KV key — closing the KV injection). The unknown/forged
        // net never reaches the floor map either, closing the unbounded-net DoS.
        Assert.Empty(h.Signal.Published);
        Assert.Empty(h.Writer.Written);
    }

    [Fact]
    public async Task A_non_member_cannot_seize_an_idle_net_held_by_no_one()
    {
        // Floor-hijack guard: even an idle net cannot be grabbed by a participant who is not a member of it.
        Harness h = Build();

        // 'challenger' is a member of 'alpha' (so the gate admits it) — prove a legit member IS granted...
        await h.Arbiter.HandleRequestAsync(Request("alpha", "challenger", 5), Ct);
        Assert.Equal(FloorOutcomes.Granted, Assert.Single(h.Signal.Published).Outcome);

        // ...but a request for a net the participant is not on is dropped, leaving the real net untouched.
        await h.Arbiter.HandleRequestAsync(Request("bravo", "challenger", 9), Ct);
        Assert.Single(h.Signal.Published); // still just the one granted event for 'alpha'
    }

    [Theory]
    [InlineData(">", "p1")]            // forged/wildcard NetId
    [InlineData("a.b", "p1")]          // dotted NetId (KV-key injection attempt)
    [InlineData("bravo", "p1")]        // p1 is a member of 'alpha', not 'bravo'
    [InlineData("alpha", "outsider")]  // unknown participant
    public async Task A_release_for_a_net_the_participant_is_not_a_member_of_is_dropped(string net, string participant)
    {
        Harness h = Build();

        await h.Arbiter.HandleReleaseAsync(new FloorReleaseMessage(net, participant), Ct);

        Assert.Empty(h.Signal.Published);
        Assert.Empty(h.Writer.Written);
    }

    [Fact]
    public async Task A_stale_release_reordered_after_a_re_request_emits_no_released_event()
    {
        // The cross-stream race end-to-end: p1 holds (seq 1); a fast PTT bounce re-presses (seq 2), and a
        // transport reorder delivers that re-request before the earlier release. The stale release (seq 1)
        // must be ignored — no spurious 'released' broadcast that would drop the live transmission.
        Harness h = Build();
        await h.Arbiter.HandleRequestAsync(new FloorRequestMessage("alpha", "p1", 5, Sequence: 1), Ct);
        await h.Arbiter.HandleRequestAsync(new FloorRequestMessage("alpha", "p1", 5, Sequence: 2), Ct);
        int published = h.Signal.Published.Count;
        int written = h.Writer.Written.Count;

        await h.Arbiter.HandleReleaseAsync(new FloorReleaseMessage("alpha", "p1", Sequence: 1), Ct);

        Assert.Equal(published, h.Signal.Published.Count); // no 'released' event emitted
        Assert.Equal(written, h.Writer.Written.Count);     // no floor_state write
        Assert.Equal("p1", h.Signal.Published[^1].CurrentHolder); // p1 still holds the floor
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
            Force,
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
