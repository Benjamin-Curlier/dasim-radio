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
}
