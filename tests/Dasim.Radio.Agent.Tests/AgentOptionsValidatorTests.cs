using Dasim.Radio.Agent;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dasim.Radio.Agent.Tests;

public sealed class AgentOptionsValidatorTests
{
    private static AgentOptions Valid() => new()
    {
        HostId = "post-01",
        HostName = "Post 01",
        HeartbeatInterval = TimeSpan.FromSeconds(5),
    };

    [Fact]
    public void Accepts_valid_options()
    {
        ValidateOptionsResult result = new AgentOptionsValidator().Validate(null, Valid());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_missing_host_id(string hostId)
    {
        AgentOptions options = Valid();
        options.HostId = hostId;

        ValidateOptionsResult result = new AgentOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData("post.01")]
    [InlineData("post*")]
    [InlineData("post>")]
    [InlineData("post 01")]
    public void Rejects_host_id_that_is_not_a_single_nats_token(string hostId)
    {
        AgentOptions options = Valid();
        options.HostId = hostId;

        ValidateOptionsResult result = new AgentOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Rejects_non_positive_heartbeat_interval()
    {
        AgentOptions options = Valid();
        options.HeartbeatInterval = TimeSpan.Zero;

        ValidateOptionsResult result = new AgentOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
    }
}
