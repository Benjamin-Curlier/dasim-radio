using Dasim.Radio.Client;
using Xunit;

namespace Dasim.Radio.Client.Tests;

public sealed class FloorStateMachineTests
{
    private static FloorStateMachine InPhase(PttPhase phase)
    {
        var machine = new FloorStateMachine();
        if (phase == PttPhase.Idle)
        {
            return machine;
        }

        machine.OnPttPressed(); // → Requesting
        switch (phase)
        {
            case PttPhase.Transmitting:
                machine.OnFloor(FloorInput.GrantedToUs);
                break;
            case PttPhase.Denied:
                machine.OnFloor(FloorInput.DeniedToUs);
                break;
            case PttPhase.Preempted:
                machine.OnFloor(FloorInput.GrantedToUs);
                machine.OnFloor(FloorInput.LostFloor);
                break;
            default:
                break; // Requesting
        }

        Assert.Equal(phase, machine.Phase);
        return machine;
    }

    [Fact]
    public void Press_from_idle_requests_the_floor()
    {
        var machine = new FloorStateMachine();

        FloorEffect effect = machine.OnPttPressed();

        Assert.Equal(FloorEffect.SendRequest, effect);
        Assert.Equal(PttPhase.Requesting, machine.Phase);
        Assert.False(machine.IsTransmitting);
    }

    [Fact]
    public void A_second_press_while_requesting_is_a_noop()
    {
        FloorStateMachine machine = InPhase(PttPhase.Requesting);

        FloorEffect effect = machine.OnPttPressed();

        Assert.Equal(FloorEffect.None, effect);
        Assert.Equal(PttPhase.Requesting, machine.Phase);
    }

    [Fact]
    public void Grant_promotes_a_request_to_transmitting()
    {
        FloorStateMachine machine = InPhase(PttPhase.Requesting);

        FloorEffect effect = machine.OnFloor(FloorInput.GrantedToUs);

        Assert.Equal(FloorEffect.None, effect);
        Assert.Equal(PttPhase.Transmitting, machine.Phase);
        Assert.True(machine.IsTransmitting);
    }

    [Theory]
    [InlineData(FloorInput.DeniedToUs)]
    [InlineData(FloorInput.LostFloor)]
    public void A_request_that_does_not_win_becomes_denied(FloorInput input)
    {
        FloorStateMachine machine = InPhase(PttPhase.Requesting);

        machine.OnFloor(input);

        Assert.Equal(PttPhase.Denied, machine.Phase);
        Assert.False(machine.IsTransmitting);
    }

    [Fact]
    public void Losing_the_floor_while_transmitting_is_a_preemption()
    {
        FloorStateMachine machine = InPhase(PttPhase.Transmitting);

        machine.OnFloor(FloorInput.LostFloor);

        Assert.Equal(PttPhase.Preempted, machine.Phase);
        Assert.False(machine.IsTransmitting);
    }

    [Theory]
    [InlineData(PttPhase.Denied)]
    [InlineData(PttPhase.Preempted)]
    public void The_net_going_idle_while_still_pressed_re_requests(PttPhase blocked)
    {
        FloorStateMachine machine = InPhase(blocked);

        FloorEffect effect = machine.OnFloor(FloorInput.NetIdle);

        Assert.Equal(FloorEffect.SendRequest, effect);
        Assert.Equal(PttPhase.Requesting, machine.Phase);
    }

    [Theory]
    [InlineData(PttPhase.Denied)]
    [InlineData(PttPhase.Preempted)]
    public void A_direct_grant_while_blocked_starts_transmitting(PttPhase blocked)
    {
        // The authority can grant us the net without an intervening idle event; we must honour it.
        FloorStateMachine machine = InPhase(blocked);

        FloorEffect effect = machine.OnFloor(FloorInput.GrantedToUs);

        Assert.Equal(FloorEffect.None, effect);
        Assert.Equal(PttPhase.Transmitting, machine.Phase);
        Assert.True(machine.IsTransmitting);
    }

    [Theory]
    [InlineData(PttPhase.Requesting)]
    [InlineData(PttPhase.Transmitting)]
    public void Release_while_engaged_releases_the_floor(PttPhase phase)
    {
        FloorStateMachine machine = InPhase(phase);

        FloorEffect effect = machine.OnPttReleased();

        Assert.Equal(FloorEffect.SendRelease, effect);
        Assert.Equal(PttPhase.Idle, machine.Phase);
        Assert.False(machine.IsTransmitting);
    }

    [Theory]
    [InlineData(PttPhase.Denied)]
    [InlineData(PttPhase.Preempted)]
    public void Release_while_blocked_just_goes_idle_without_chatter(PttPhase blocked)
    {
        FloorStateMachine machine = InPhase(blocked);

        FloorEffect effect = machine.OnPttReleased();

        Assert.Equal(FloorEffect.None, effect); // nothing held and no pending request — no release needed
        Assert.Equal(PttPhase.Idle, machine.Phase);
    }

    [Fact]
    public void Release_from_idle_is_a_noop()
    {
        var machine = new FloorStateMachine();

        FloorEffect effect = machine.OnPttReleased();

        Assert.Equal(FloorEffect.None, effect);
        Assert.Equal(PttPhase.Idle, machine.Phase);
    }

    [Theory]
    [InlineData(FloorInput.NetIdle)]
    [InlineData(FloorInput.LostFloor)]
    [InlineData(FloorInput.GrantedToUs)]
    public void Floor_events_while_idle_do_not_change_phase(FloorInput input)
    {
        var machine = new FloorStateMachine();

        FloorEffect effect = machine.OnFloor(input);

        Assert.Equal(FloorEffect.None, effect);
        Assert.Equal(PttPhase.Idle, machine.Phase);
    }
}
