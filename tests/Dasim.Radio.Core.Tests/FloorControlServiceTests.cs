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
}
