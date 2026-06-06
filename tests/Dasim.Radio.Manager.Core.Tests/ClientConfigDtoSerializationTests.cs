using System.Text.Json;
using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.Serialization;
using Xunit;

namespace Dasim.Radio.Manager.Core.Tests;

public sealed class ClientConfigDtoSerializationTests
{
    [Fact]
    public void Round_trips_through_the_source_generated_wire_serializer()
    {
        var dto = new ClientConfigDto("cfg1", "c1", "p1", "alpha", ParentNetId: null, "Alpha One", HostId: "post-01");

        string json = JsonSerializer.Serialize(dto, RadioJsonContext.Default.ClientConfigDto);
        ClientConfigDto? back = JsonSerializer.Deserialize(json, RadioJsonContext.Default.ClientConfigDto);

        Assert.Contains("configId", json); // camelCase wire naming
        Assert.Equal(dto, back); // includes the null ParentNetId and the non-null HostId
    }

    [Fact]
    public void Round_trips_with_a_parent_net_and_no_host()
    {
        var dto = new ClientConfigDto("cfg2", "c2", "p2", "bravo", ParentNetId: "root", "Bravo Two", HostId: null);

        string json = JsonSerializer.Serialize(dto, RadioJsonContext.Default.ClientConfigDto);
        ClientConfigDto? back = JsonSerializer.Deserialize(json, RadioJsonContext.Default.ClientConfigDto);

        Assert.Equal(dto, back);
    }
}
