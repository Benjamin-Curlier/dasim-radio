using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Routing;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dasim.Radio.MediaService.Tests;

public sealed class FloorControlHoldersTests
{
    [Fact]
    public void Maps_held_floors_to_mix_sources()
    {
        var floor = new FloorControlService(new FakeTimeProvider());
        floor.RequestFloor(new NetId("A1a"), new ParticipantId("p1"), new Priority(20));
        var sut = new FloorControlHolders(floor);

        FloorHolders holders = sut.Current();

        Assert.True(holders.TryGetHolder(new NetId("A1a"), out MixSource source));
        Assert.Equal(new ParticipantId("p1"), source.Speaker);
        Assert.Equal(new Priority(20), source.Priority);
    }

    [Fact]
    public void Omits_idle_nets()
    {
        var floor = new FloorControlService(new FakeTimeProvider());
        var net = new NetId("A1a");
        var participant = new ParticipantId("p1");
        floor.RequestFloor(net, participant, new Priority(20));
        floor.ReleaseFloor(net, participant);
        var sut = new FloorControlHolders(floor);

        Assert.Empty(sut.Current().ActiveNets);
    }

    [Fact]
    public void Reuses_the_cached_snapshot_until_the_floor_changes()
    {
        var floor = new FloorControlService(new FakeTimeProvider());
        floor.RequestFloor(new NetId("A1a"), new ParticipantId("p1"), new Priority(20));
        var sut = new FloorControlHolders(floor);

        FloorHolders first = sut.Current();
        FloorHolders second = sut.Current();

        // No floor change between the calls => the exact same snapshot is handed back (no rebuild).
        Assert.Same(first, second);
    }

    [Fact]
    public void Rebuilds_the_snapshot_after_a_floor_change()
    {
        var floor = new FloorControlService(new FakeTimeProvider());
        var net = new NetId("A1a");
        floor.RequestFloor(net, new ParticipantId("p1"), new Priority(20));
        var sut = new FloorControlHolders(floor);

        FloorHolders before = sut.Current();
        floor.RequestFloor(new NetId("A1"), new ParticipantId("A1"), new Priority(60));
        FloorHolders after = sut.Current();

        Assert.NotSame(before, after);
        Assert.True(after.TryGetHolder(new NetId("A1"), out MixSource added));
        Assert.Equal(new ParticipantId("A1"), added.Speaker);
    }
}
