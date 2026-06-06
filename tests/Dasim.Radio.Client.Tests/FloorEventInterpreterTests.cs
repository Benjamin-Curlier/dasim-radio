using Dasim.Radio.Client;
using Dasim.Radio.Contracts;
using Xunit;

namespace Dasim.Radio.Client.Tests;

public sealed class FloorEventInterpreterTests
{
    private const string Me = "p1";

    [Fact]
    public void Holder_is_us_means_granted()
    {
        var @event = new FloorEventMessage("alpha", FloorOutcomes.Granted, Me, null, Me);

        Assert.Equal(FloorInput.GrantedToUs, FloorEventInterpreter.Interpret(@event, Me));
    }

    [Fact]
    public void Our_request_denied_means_denied_to_us()
    {
        var @event = new FloorEventMessage("alpha", FloorOutcomes.Denied, Me, null, "bob");

        Assert.Equal(FloorInput.DeniedToUs, FloorEventInterpreter.Interpret(@event, Me));
    }

    [Fact]
    public void No_holder_means_the_net_is_idle()
    {
        var @event = new FloorEventMessage("alpha", FloorOutcomes.Released, "bob", null, null);

        Assert.Equal(FloorInput.NetIdle, FloorEventInterpreter.Interpret(@event, Me));
    }

    [Fact]
    public void Another_holder_means_lost_floor()
    {
        var @event = new FloorEventMessage("alpha", FloorOutcomes.GrantedWithPreemption, "bob", Me, "bob");

        Assert.Equal(FloorInput.LostFloor, FloorEventInterpreter.Interpret(@event, Me));
    }
}
