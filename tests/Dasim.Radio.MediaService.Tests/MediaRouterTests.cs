using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Routing;
using Xunit;
using static Dasim.Radio.MediaService.Tests.RoutingSample;

namespace Dasim.Radio.MediaService.Tests;

public sealed class MediaRouterTests
{
    private static readonly IMixPolicy Override = new PriorityOverrideMixPolicy();
    private static readonly IMixPolicy Additive = new AdditiveMixPolicy();

    private static MediaRouter Router(FloorHolders holders, IMixPolicy policy) =>
        new(new FakeForceTreeProvider(BuildRouting()), new FakeFloorHolders(holders), policy);

    private static MixSource Holder(string net, string speaker, int priority) =>
        new(Participant(speaker), Net(net), new Priority(priority));

    private static string[] Listeners(IEnumerable<MixDelivery> deliveries) =>
        deliveries.Select(d => d.Listener.Value).OrderBy(v => v, StringComparer.Ordinal).ToArray();

    [Fact]
    public void Delivers_a_holders_frame_to_the_other_net_members()
    {
        FloorHolders holders = FloorHolders.From([Holder(A1a, P1, 20)]);

        IReadOnlyList<MixDelivery> deliveries = Router(holders, Override).Deliveries(Participant(P1));

        Assert.Equal([A1a, P2], Listeners(deliveries)); // p1 excluded (self)
    }

    [Fact]
    public void A_speaker_without_the_floor_drives_nothing()
    {
        FloorHolders holders = FloorHolders.From([Holder(A1a, A1a, 40)]);

        Assert.Empty(Router(holders, Override).Deliveries(Participant(P2)));
    }

    [Fact]
    public void An_unknown_speaker_drives_nothing()
    {
        FloorHolders holders = FloorHolders.From([Holder(A1a, P1, 20)]);

        Assert.Empty(Router(holders, Override).Deliveries(Participant("ghost")));
    }

    [Fact]
    public void Override_suppresses_a_lower_net_for_members_hearing_a_superior()
    {
        FloorHolders holders = FloorHolders.From([Holder(A1a, P1, 20), Holder(A1, A1, 60)]);
        MediaRouter router = Router(holders, Override);

        // p1's frame reaches p2 only; the group leader hears the section leader instead.
        Assert.Equal([P2], Listeners(router.Deliveries(Participant(P1))));
        // A1's frame reaches both group leaders.
        Assert.Equal([A1a, A1b], Listeners(router.Deliveries(Participant(A1))));
    }

    [Fact]
    public void Additive_drives_a_multi_source_listener_only_from_its_trigger()
    {
        // p1 on the group net (20) and section leader A1 on the section net (60). The group leader A1a
        // is on both nets, so under additive it hears both summed — driven once, by its trigger (the
        // higher-priority source, A1), not by p1.
        FloorHolders holders = FloorHolders.From([Holder(A1a, P1, 20), Holder(A1, A1, 60)]);
        MediaRouter router = Router(holders, Additive);

        // p1's frame is not A1a's trigger, so it only drives p2 (group-net only).
        Assert.Equal([P2], Listeners(router.Deliveries(Participant(P1))));

        // A1's frame triggers A1a's mix, carrying BOTH sources to sum.
        MixDelivery toLeader = Assert.Single(
            router.Deliveries(Participant(A1)), delivery => delivery.Listener == Participant(A1a));
        string[] sources = toLeader.Sources
            .Select(s => s.Speaker.Value).OrderBy(v => v, StringComparer.Ordinal).ToArray();
        Assert.Equal([A1, P1], sources);
    }
}
