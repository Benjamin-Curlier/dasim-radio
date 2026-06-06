using Dasim.Radio.Core;
using Xunit;
using static Dasim.Radio.Core.Tests.SampleForce;

namespace Dasim.Radio.Core.Tests;

public sealed class MixPlannerTests
{
    private static readonly IMixPolicy Additive = new AdditiveMixPolicy();
    private static readonly IMixPolicy Override = new PriorityOverrideMixPolicy();

    private readonly MixPlanner _sut = new(BuildTopology());

    private static MixSource Holder(string net, string speaker, int priority) =>
        new(Participant(speaker), Net(net), new Priority(priority));

    [Fact]
    public void A_listener_hears_the_holder_of_a_net_they_are_on()
    {
        FloorHolders holders = FloorHolders.From([Holder(A1a, A1a, 40)]);

        MixPlan plan = _sut.PlanFor(Participant(P1), holders, Override);

        Assert.Equal(Participant(A1a), Assert.Single(plan.Sources).Speaker);
    }

    [Fact]
    public void A_listener_never_hears_their_own_transmission()
    {
        // A1a holds its own net (talking down); the parent section net is idle.
        FloorHolders holders = FloorHolders.From([Holder(A1a, A1a, 40)]);

        MixPlan plan = _sut.PlanFor(Participant(A1a), holders, Additive);

        Assert.True(plan.IsSilent);
    }

    [Fact]
    public void A_listener_ignores_holders_of_nets_they_are_not_on()
    {
        // p1 is on net A1a only; a holder on the section net A1 must not reach p1.
        FloorHolders holders = FloorHolders.From([Holder(A1, A1, 60)]);

        Assert.True(_sut.PlanFor(Participant(P1), holders, Additive).IsSilent);
    }

    [Fact]
    public void A_leader_hears_their_superior_on_the_parent_net_while_holding_their_own()
    {
        // A1a talks down (holds A1a); section leader A1 talks on the section net A1.
        FloorHolders holders = FloorHolders.From([Holder(A1a, A1a, 40), Holder(A1, A1, 60)]);

        MixPlan plan = _sut.PlanFor(Participant(A1a), holders, Override);

        // Hears the superior, not itself.
        Assert.Equal(Participant(A1), Assert.Single(plan.Sources).Speaker);
    }

    [Fact]
    public void Additive_policy_sums_both_active_nets_a_listener_is_on()
    {
        // p1 talks up on A1a; section leader A1 talks on the section net. A1a is on both nets.
        FloorHolders holders = FloorHolders.From([Holder(A1a, P1, 20), Holder(A1, A1, 60)]);

        MixPlan plan = _sut.PlanFor(Participant(A1a), holders, Additive);

        string[] speakers = plan.Sources
            .Select(s => s.Speaker.Value).OrderBy(v => v, StringComparer.Ordinal).ToArray();
        Assert.Equal([A1, P1], speakers);
    }

    [Fact]
    public void Override_policy_keeps_only_the_highest_priority_source()
    {
        FloorHolders holders = FloorHolders.From([Holder(A1a, P1, 20), Holder(A1, A1, 60)]);

        MixPlan plan = _sut.PlanFor(Participant(A1a), holders, Override);

        MixSource source = Assert.Single(plan.Sources);
        Assert.Equal(Participant(A1), source.Speaker);
        Assert.Equal(new Priority(60), source.Priority);
    }

    [Fact]
    public void A_listener_is_silent_when_no_net_is_active()
    {
        Assert.True(_sut.PlanFor(Participant(P1), FloorHolders.Empty, Override).IsSilent);
    }

    [Fact]
    public void An_unknown_listener_is_silent()
    {
        FloorHolders holders = FloorHolders.From([Holder(A1a, A1a, 40)]);

        Assert.True(_sut.PlanFor(Participant("ghost"), holders, Additive).IsSilent);
    }

    [Fact]
    public void Floor_holders_index_active_nets_and_their_holders()
    {
        FloorHolders holders = FloorHolders.From([Holder(A1a, A1a, 40), Holder(A1, A1, 60)]);

        Assert.Equal(2, holders.ActiveNets.Count);
        Assert.True(holders.TryGetHolder(Net(A1), out MixSource held));
        Assert.Equal(Participant(A1), held.Speaker);
        Assert.False(holders.TryGetHolder(Net("ghost"), out _));
    }

    [Fact]
    public void Plan_into_matches_plan_for_under_both_policies()
    {
        FloorHolders holders = FloorHolders.From([Holder(A1a, P1, 20), Holder(A1, A1, 60)]);
        var buffer = new List<MixSource>();

        foreach (IMixPolicy policy in (IMixPolicy[])[Additive, Override])
        {
            MixPlan expected = _sut.PlanFor(Participant(A1a), holders, policy);
            _sut.PlanInto(Participant(A1a), holders, policy, buffer);

            Assert.Equal(
                expected.Sources.Select(s => s.Speaker.Value).OrderBy(v => v, StringComparer.Ordinal),
                buffer.Select(s => s.Speaker.Value).OrderBy(v => v, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void Plan_into_clears_the_buffer_when_the_listener_hears_nothing()
    {
        var buffer = new List<MixSource> { Holder("stale", "stale", 1) };

        _sut.PlanInto(Participant(P1), FloorHolders.Empty, Override, buffer);

        Assert.Empty(buffer);
    }
}
