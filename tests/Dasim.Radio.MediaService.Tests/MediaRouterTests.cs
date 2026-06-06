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

    private static string[] Sorted(IEnumerable<ParticipantId> ids) =>
        ids.Select(p => p.Value).OrderBy(v => v, StringComparer.Ordinal).ToArray();

    [Fact]
    public void Forwards_a_holders_frame_to_the_other_net_members()
    {
        // p1 holds the group net A1a; the group leader and p2 hear it (p1 not echoed to itself).
        FloorHolders holders = FloorHolders.From([Holder(A1a, P1, 20)]);

        IReadOnlyList<ParticipantId> recipients = Router(holders, Override).Recipients(Participant(P1));

        Assert.Equal([A1a, P2], Sorted(recipients));
    }

    [Fact]
    public void A_speaker_without_the_floor_is_dropped()
    {
        // The group leader holds the net; p2 (no floor) transmitting reaches nobody.
        FloorHolders holders = FloorHolders.From([Holder(A1a, A1a, 40)]);

        Assert.Empty(Router(holders, Override).Recipients(Participant(P2)));
    }

    [Fact]
    public void An_unknown_speaker_reaches_nobody()
    {
        FloorHolders holders = FloorHolders.From([Holder(A1a, P1, 20)]);

        Assert.Empty(Router(holders, Override).Recipients(Participant("ghost")));
    }

    [Fact]
    public void Override_suppresses_a_lower_net_for_members_hearing_a_superior()
    {
        // p1 talks on the group net (20); the section leader A1 talks on the section net (60).
        FloorHolders holders = FloorHolders.From([Holder(A1a, P1, 20), Holder(A1, A1, 60)]);
        MediaRouter router = Router(holders, Override);

        // p1's frame reaches p2 (group-net only) but NOT the group leader A1a, who is hearing A1.
        Assert.Equal([P2], Sorted(router.Recipients(Participant(P1))));

        // A1's frame reaches both group leaders (members of the section net).
        Assert.Equal([A1a, A1b], Sorted(router.Recipients(Participant(A1))));
    }

    [Fact]
    public void Additive_keeps_a_lower_net_for_members_also_on_a_higher_net()
    {
        // Same holders, additive policy: the group leader A1a now hears the group too. (Slice 2a only
        // forwards bytes; summing the two sources is a later slice — this proves the policy seam.)
        FloorHolders holders = FloorHolders.From([Holder(A1a, P1, 20), Holder(A1, A1, 60)]);

        IReadOnlyList<ParticipantId> recipients = Router(holders, Additive).Recipients(Participant(P1));

        Assert.Equal([A1a, P2], Sorted(recipients));
    }
}
