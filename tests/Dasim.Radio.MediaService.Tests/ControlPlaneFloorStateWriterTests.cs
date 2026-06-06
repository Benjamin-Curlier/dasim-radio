using Dasim.Radio.Contracts;
using Dasim.Radio.MediaService.Floor;
using Xunit;

namespace Dasim.Radio.MediaService.Tests;

public sealed class ControlPlaneFloorStateWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WriteAsync_puts_the_state_into_floor_state_keyed_by_net()
    {
        var bucket = new FakeFloorStateBucket();
        var writer = new ControlPlaneFloorStateWriter(new FakeControlPlaneStore(bucket));
        var state = new FloorStateDto("alpha", "p1", 5, DateTimeOffset.UnixEpoch);

        await writer.WriteAsync(state, Ct);

        (string key, FloorStateDto value) = Assert.Single(bucket.Puts);
        Assert.Equal("alpha", key);
        Assert.Equal(state, value);
    }

    [Fact]
    public async Task WriteAsync_rejects_null_state()
    {
        var writer = new ControlPlaneFloorStateWriter(new FakeControlPlaneStore(new FakeFloorStateBucket()));

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await writer.WriteAsync(null!, Ct));
    }
}
