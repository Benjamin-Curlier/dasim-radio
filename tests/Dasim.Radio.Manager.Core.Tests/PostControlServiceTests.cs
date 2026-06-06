using Dasim.Radio.Contracts;
using Dasim.Radio.Manager.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dasim.Radio.Manager.Core.Tests;

public sealed class PostControlServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(PostControlService Service, FakeAgentCommandClient Agent);

    private static async Task<Harness> BuildAsync(bool withConfig = true)
    {
        var configBucket = new FakeKeyValueStore<ClientConfigDto>(Subjects.Buckets.Configs);
        var configs = new ClientConfigService(
            new FakeControlPlaneStore(configs: configBucket), NullLogger<ClientConfigService>.Instance);
        if (withConfig)
        {
            await configs.CreateAsync(new ClientConfigDto("cfg1", "c1", "p1", "alpha", null, "Alpha One"), Ct);
        }

        var agent = new FakeAgentCommandClient();
        return new Harness(new PostControlService(agent, configs, NullLogger<PostControlService>.Instance), agent);
    }

    [Fact]
    public async Task Launch_with_a_known_config_commands_the_agent()
    {
        Harness h = await BuildAsync();

        AgentCommandResult result = await h.Service.LaunchAsync("post-01", "cfg1", Ct);

        Assert.True(result.Accepted);
        (string hostId, AgentCommandEnvelope command) = Assert.Single(h.Agent.Sent);
        Assert.Equal("post-01", hostId);
        Assert.Equal(AgentCommandKinds.Launch, command.Kind);
        Assert.Equal("cfg1", command.ConfigId);
    }

    [Fact]
    public async Task Launch_with_an_unknown_config_is_declined_locally()
    {
        Harness h = await BuildAsync(withConfig: false);

        AgentCommandResult result = await h.Service.LaunchAsync("post-01", "ghost", Ct);

        Assert.False(result.Accepted);
        Assert.Contains("Unknown config", result.Error);
        Assert.Empty(h.Agent.Sent); // the agent is never contacted
    }

    [Fact]
    public async Task Reconfigure_sends_a_reconfigure_envelope()
    {
        Harness h = await BuildAsync();

        await h.Service.ReconfigureAsync("post-01", "cfg1", Ct);

        AgentCommandEnvelope command = h.Agent.Sent.Single().Command;
        Assert.Equal(AgentCommandKinds.Reconfigure, command.Kind);
        Assert.Equal("cfg1", command.ConfigId);
    }

    [Fact]
    public async Task Stop_commands_the_agent()
    {
        Harness h = await BuildAsync();

        await h.Service.StopAsync("post-01", Ct);

        Assert.Equal(AgentCommandKinds.Stop, h.Agent.Sent.Single().Command.Kind);
    }

    [Fact]
    public async Task A_bad_host_id_is_rejected()
    {
        Harness h = await BuildAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => h.Service.StopAsync("post.01", Ct).AsTask());
    }

    [Fact]
    public async Task An_agent_decline_is_surfaced_unchanged()
    {
        Harness h = await BuildAsync();
        h.Agent.Result = new AgentCommandResult(false, "busy");

        AgentCommandResult result = await h.Service.LaunchAsync("post-01", "cfg1", Ct);

        Assert.False(result.Accepted);
        Assert.Equal("busy", result.Error);
    }
}
