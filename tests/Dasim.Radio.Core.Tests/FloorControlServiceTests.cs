using System.Globalization;
using Dasim.Radio.Core;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dasim.Radio.Core.Tests;

public sealed class FloorControlServiceTests
{
    private static readonly NetId Net = new("section-1");

    private readonly FakeTimeProvider _clock = new();
    private readonly FloorControlService _sut;

    public FloorControlServiceTests() => _sut = new FloorControlService(_clock);

    [Fact]
    public void Idle_net_grants_first_requester()
    {
        FloorDecision decision = _sut.RequestFloor(Net, new ParticipantId("soldier"), new Priority(1));

        Assert.Equal(FloorOutcome.Granted, decision.Outcome);
        Assert.True(decision.IsGranted);
        Assert.Null(decision.PreemptedParticipant);
    }

    [Fact]
    public void Re_request_by_same_holder_is_idempotent_grant()
    {
        var participant = new ParticipantId("soldier");
        _sut.RequestFloor(Net, participant, new Priority(1));

        FloorDecision again = _sut.RequestFloor(Net, participant, new Priority(1));

        Assert.Equal(FloorOutcome.Granted, again.Outcome);
        Assert.Equal(participant, _sut.GetSnapshot(Net).Holder);
    }

    [Fact]
    public void Higher_priority_preempts_current_holder()
    {
        var subordinate = new ParticipantId("section-leader");
        var chief = new ParticipantId("company-chief");
        _sut.RequestFloor(Net, subordinate, new Priority(1));

        FloorDecision decision = _sut.RequestFloor(Net, chief, new Priority(5));

        Assert.Equal(FloorOutcome.GrantedWithPreemption, decision.Outcome);
        Assert.Equal(subordinate, decision.PreemptedParticipant);
        Assert.Equal(chief, _sut.GetSnapshot(Net).Holder);
    }

    [Fact]
    public void Lower_priority_is_denied_while_higher_holds()
    {
        var chief = new ParticipantId("company-chief");
        var subordinate = new ParticipantId("section-leader");
        _sut.RequestFloor(Net, chief, new Priority(5));

        FloorDecision decision = _sut.RequestFloor(Net, subordinate, new Priority(1));

        Assert.Equal(FloorOutcome.Denied, decision.Outcome);
        Assert.False(decision.IsGranted);
        Assert.Equal(chief, _sut.GetSnapshot(Net).Holder);
    }

    [Fact]
    public void Equal_priority_does_not_preempt()
    {
        var first = new ParticipantId("a");
        var second = new ParticipantId("b");
        _sut.RequestFloor(Net, first, new Priority(3));

        FloorDecision decision = _sut.RequestFloor(Net, second, new Priority(3));

        Assert.Equal(FloorOutcome.Denied, decision.Outcome);
        Assert.Equal(first, _sut.GetSnapshot(Net).Holder);
    }

    [Fact]
    public void Release_by_holder_frees_the_floor()
    {
        var participant = new ParticipantId("a");
        _sut.RequestFloor(Net, participant, new Priority(1));

        _sut.ReleaseFloor(Net, participant);

        Assert.Equal(FloorStatus.Idle, _sut.GetSnapshot(Net).Status);
    }

    [Fact]
    public void Release_by_non_holder_is_ignored()
    {
        var holder = new ParticipantId("holder");
        var other = new ParticipantId("other");
        _sut.RequestFloor(Net, holder, new Priority(1));

        FloorDecision decision = _sut.ReleaseFloor(Net, other);

        Assert.False(decision.IsGranted);
        Assert.Equal(holder, _sut.GetSnapshot(Net).Holder);
    }

    [Fact]
    public void A_stale_release_reordered_after_a_re_request_does_not_free_the_floor()
    {
        // The verifier's scenario: a holder releases (PTT up) then immediately re-presses (PTT down),
        // and a transport reorder delivers the re-request (seq 2) before the earlier release (seq 1).
        var holder = new ParticipantId("holder");
        _sut.RequestFloor(Net, holder, new Priority(1), sequence: 1); // press 1 -> held (seq 1)
        _sut.RequestFloor(Net, holder, new Priority(1), sequence: 2); // re-press -> still held, now seq 2

        FloorDecision stale = _sut.ReleaseFloor(Net, holder, sequence: 1); // press 1's late release

        Assert.False(stale.IsGranted); // rejected as superseded
        Assert.Equal(FloorStatus.Held, _sut.GetSnapshot(Net).Status);
        Assert.Equal(holder, _sut.GetSnapshot(Net).Holder); // the operator keeps the floor (PTT still down)
    }

    [Fact]
    public void A_release_newer_than_the_held_press_is_rejected()
    {
        // Inverse of the stale-release race: a release whose sequence is NEWER than the held press (e.g.
        // one that overtook its own request) must not free the live hold either — only the exact held
        // press frees it.
        var holder = new ParticipantId("holder");
        _sut.RequestFloor(Net, holder, new Priority(1), sequence: 5);

        FloorDecision release = _sut.ReleaseFloor(Net, holder, sequence: 7);

        Assert.False(release.IsGranted);
        Assert.Equal(FloorStatus.Held, _sut.GetSnapshot(Net).Status);
        Assert.Equal(holder, _sut.GetSnapshot(Net).Holder);
    }

    [Fact]
    public void The_release_matching_the_current_press_frees_the_floor()
    {
        var holder = new ParticipantId("holder");
        _sut.RequestFloor(Net, holder, new Priority(1), sequence: 1);
        _sut.RequestFloor(Net, holder, new Priority(1), sequence: 2);

        FloorDecision release = _sut.ReleaseFloor(Net, holder, sequence: 2); // the current press's release

        Assert.True(release.IsGranted);
        Assert.Equal(FloorStatus.Idle, _sut.GetSnapshot(Net).Status);
    }

    [Fact]
    public void A_stale_release_does_not_advance_the_version()
    {
        var holder = new ParticipantId("holder");
        _sut.RequestFloor(Net, holder, new Priority(1), sequence: 1);
        _sut.RequestFloor(Net, holder, new Priority(1), sequence: 2);
        long version = _sut.Version;

        _sut.ReleaseFloor(Net, holder, sequence: 1); // stale -> ignored

        Assert.Equal(version, _sut.Version); // held set unchanged: the router's cache must not be invalidated
    }

    [Fact]
    public void A_holder_that_re_requests_with_a_lower_sequence_can_still_release()
    {
        // Restart safety: if a client's counter resets (process restart) and it re-asserts an existing
        // hold with a lower sequence, the matching release must still free the floor — never wedge it.
        var holder = new ParticipantId("holder");
        _sut.RequestFloor(Net, holder, new Priority(1), sequence: 5); // held seq 5
        _sut.RequestFloor(Net, holder, new Priority(1), sequence: 1); // re-assert after a counter reset

        FloorDecision release = _sut.ReleaseFloor(Net, holder, sequence: 1);

        Assert.True(release.IsGranted);
        Assert.Equal(FloorStatus.Idle, _sut.GetSnapshot(Net).Status);
    }

    [Fact]
    public void Floor_can_be_reacquired_after_release()
    {
        var first = new ParticipantId("a");
        var second = new ParticipantId("b");
        _sut.RequestFloor(Net, first, new Priority(2));
        _sut.ReleaseFloor(Net, first);

        FloorDecision decision = _sut.RequestFloor(Net, second, new Priority(1));

        Assert.Equal(FloorOutcome.Granted, decision.Outcome);
        Assert.Equal(second, _sut.GetSnapshot(Net).Holder);
    }

    [Fact]
    public void Snapshot_records_hold_time_from_clock()
    {
        _clock.SetUtcNow(DateTimeOffset.Parse("2026-06-04T10:00:00Z", CultureInfo.InvariantCulture));
        var participant = new ParticipantId("a");

        _sut.RequestFloor(Net, participant, new Priority(1));

        Assert.Equal(_clock.GetUtcNow(), _sut.GetSnapshot(Net).HeldSince);
    }

    [Fact]
    public void ActiveFloors_is_empty_when_no_net_is_held()
    {
        Assert.Empty(_sut.ActiveFloors());
    }

    [Fact]
    public void Version_advances_on_grant_preemption_and_release_but_not_on_denial()
    {
        long start = _sut.Version;
        var subordinate = new ParticipantId("sub");
        var chief = new ParticipantId("chief");
        var nobody = new ParticipantId("nobody");

        _sut.RequestFloor(Net, subordinate, new Priority(1)); // grant
        long afterGrant = _sut.Version;
        Assert.True(afterGrant > start);

        _sut.RequestFloor(Net, nobody, new Priority(1)); // denied (equal priority)
        Assert.Equal(afterGrant, _sut.Version);

        _sut.RequestFloor(Net, chief, new Priority(5)); // pre-emption
        long afterPreempt = _sut.Version;
        Assert.True(afterPreempt > afterGrant);

        _sut.ReleaseFloor(Net, nobody); // not the holder => ignored
        Assert.Equal(afterPreempt, _sut.Version);

        _sut.ReleaseFloor(Net, chief); // release
        Assert.True(_sut.Version > afterPreempt);
    }

    [Fact]
    public void Version_does_not_advance_on_a_same_priority_re_request()
    {
        var holder = new ParticipantId("holder");
        _sut.RequestFloor(Net, holder, new Priority(3));
        long afterGrant = _sut.Version;

        // A keep-alive re-press at the same priority changes nothing — it must not invalidate the cache.
        FloorDecision again = _sut.RequestFloor(Net, holder, new Priority(3));

        Assert.Equal(FloorOutcome.Granted, again.Outcome);
        Assert.Equal(afterGrant, _sut.Version);
    }

    [Fact]
    public void Version_advances_when_the_holder_re_requests_at_a_new_priority()
    {
        var holder = new ParticipantId("holder");
        _sut.RequestFloor(Net, holder, new Priority(3));
        long afterGrant = _sut.Version;

        // The holder's priority is part of what the router mixes on, so a refresh is a real change.
        _sut.RequestFloor(Net, holder, new Priority(7));

        Assert.True(_sut.Version > afterGrant);
    }

    [Fact]
    public void ActiveFloors_lists_only_held_nets()
    {
        var netA = new NetId("net-a");
        var netB = new NetId("net-b");
        _sut.RequestFloor(netA, new ParticipantId("x"), new Priority(2));
        _sut.RequestFloor(netB, new ParticipantId("y"), new Priority(3));
        _sut.ReleaseFloor(netB, new ParticipantId("y"));

        FloorSnapshot held = Assert.Single(_sut.ActiveFloors());

        Assert.Equal(netA, held.Net);
        Assert.Equal(new ParticipantId("x"), held.Holder);
        Assert.Equal(new Priority(2), held.HolderPriority);
    }
}
