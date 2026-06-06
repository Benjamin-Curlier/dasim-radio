using Dasim.Radio.Core;
using Xunit;
using static Dasim.Radio.Core.Tests.SampleForce;

namespace Dasim.Radio.Core.Tests;

public sealed class NetTopologyTests
{
    private readonly NetTopology _sut = BuildTopology();

    [Fact]
    public void There_is_one_net_per_non_leaf_node()
    {
        string[] nets = _sut.Nets.Select(n => n.Value).OrderBy(v => v, StringComparer.Ordinal).ToArray();

        Assert.Equal([A1, A1a, A1b, A2, Alpha, Co], nets);
    }

    [Fact]
    public void A_leaf_member_is_on_its_parent_net_only()
    {
        NetMembership membership = _sut.MembershipOf(Participant(P1));

        Assert.Null(membership.OwnedNet);
        Assert.Equal(Net(A1a), membership.ParentNet);
        Assert.Equal([Net(A1a)], membership.Nets);
    }

    [Fact]
    public void A_leader_is_on_its_owned_net_and_its_parent_net()
    {
        NetMembership membership = _sut.MembershipOf(Participant(A1a));

        Assert.Equal(Net(A1a), membership.OwnedNet);
        Assert.Equal(Net(A1), membership.ParentNet);
        Assert.Equal([Net(A1a), Net(A1)], membership.Nets);
    }

    [Fact]
    public void The_root_owns_a_net_but_has_no_parent_net()
    {
        NetMembership membership = _sut.MembershipOf(Participant(Co));

        Assert.Equal(Net(Co), membership.OwnedNet);
        Assert.Null(membership.ParentNet);
        Assert.Equal([Net(Co)], membership.Nets);
    }

    [Fact]
    public void A_leaf_company_is_on_the_command_net_only()
    {
        NetMembership membership = _sut.MembershipOf(Participant(Bravo));

        Assert.Null(membership.OwnedNet);
        Assert.Equal(Net(Co), membership.ParentNet);
    }

    [Fact]
    public void Members_of_a_net_are_its_owner_plus_direct_children()
    {
        string[] members = _sut.MembersOf(Net(A1a))
            .Select(p => p.Value).OrderBy(v => v, StringComparer.Ordinal).ToArray();

        Assert.Equal([A1a, P1, P2], members);
    }

    [Fact]
    public void Members_of_a_mid_tree_net_are_the_subordinate_leaders()
    {
        string[] members = _sut.MembersOf(Net(A1))
            .Select(p => p.Value).OrderBy(v => v, StringComparer.Ordinal).ToArray();

        Assert.Equal([A1, A1a, A1b], members);
    }

    [Fact]
    public void Members_of_an_unknown_net_is_empty()
    {
        Assert.Empty(_sut.MembersOf(Net("ghost")));
    }

    [Fact]
    public void An_unknown_participant_has_an_empty_membership()
    {
        NetMembership membership = _sut.MembershipOf(Participant("ghost"));

        Assert.Empty(membership.Nets);
        Assert.Null(membership.OwnedNet);
        Assert.Null(membership.ParentNet);
        Assert.Null(membership.DefaultTransmitNet);
    }

    [Fact]
    public void Default_transmit_net_is_the_owned_net_for_a_leader()
    {
        Assert.Equal(Net(A1a), _sut.MembershipOf(Participant(A1a)).DefaultTransmitNet);
    }

    [Fact]
    public void Default_transmit_net_is_the_parent_net_for_a_leaf_member()
    {
        Assert.Equal(Net(A1a), _sut.MembershipOf(Participant(P1)).DefaultTransmitNet);
    }

    [Fact]
    public void A_single_node_tree_has_no_nets()
    {
        var solo = new ForceTree(ForceNode.Leaf("solo", "Solo", ForceNodeKind.Member, new Priority(1)));

        NetTopology topology = NetTopology.FromForceTree(solo);

        Assert.Empty(topology.Nets);
        Assert.Empty(topology.MembershipOf(Participant("solo")).Nets);
    }

    [Fact]
    public void The_empty_topology_has_no_nets_or_membership()
    {
        Assert.Empty(NetTopology.Empty.Nets);
        Assert.Empty(NetTopology.Empty.MembersOf(Net(A1)));
        Assert.Empty(NetTopology.Empty.MembershipOf(Participant("anyone")).Nets);
    }
}
