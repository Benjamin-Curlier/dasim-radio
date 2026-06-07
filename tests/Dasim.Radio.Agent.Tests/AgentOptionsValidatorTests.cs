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

    [Theory]
    [InlineData(8)]  // just over half the 15s TTL
    [InlineData(15)] // equals the TTL: the key expires before the next beat
    [InlineData(30)] // well over the TTL
    public void Rejects_heartbeat_interval_above_half_the_presence_ttl(int seconds)
    {
        AgentOptions options = Valid();
        options.HeartbeatInterval = TimeSpan.FromSeconds(seconds);

        ValidateOptionsResult result = new AgentOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(5)]    // the shipped default
    [InlineData(7)]    // just under half the 15s TTL
    [InlineData(7.5)]  // exactly half the TTL — the boundary is allowed
    public void Accepts_heartbeat_interval_at_or_below_half_the_presence_ttl(double seconds)
    {
        AgentOptions options = Valid();
        options.HeartbeatInterval = TimeSpan.FromSeconds(seconds);

        ValidateOptionsResult result = new AgentOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }
}
