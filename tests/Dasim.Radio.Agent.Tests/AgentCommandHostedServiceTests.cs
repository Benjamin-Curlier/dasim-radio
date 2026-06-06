using Dasim.Radio.Agent;
using Dasim.Radio.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dasim.Radio.Agent.Tests;

public sealed class AgentCommandHostedServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        AgentCommandHostedService Service,
        FakeAgentCommandServer Server,
        FakeClientController Controller);

    private static async Task<Harness> StartedAsync(FakeClientController? controller = null)
    {
        var server = new FakeAgentCommandServer();
        controller ??= new FakeClientController();
        var service = new AgentCommandHostedService(
            server, controller, Options.Create(new AgentOptions { HostId = "post-01" }),
            NullLogger<AgentCommandHostedService>.Instance);

        await service.StartAsync(Ct);
        return new Harness(service, server, controller);
    }

    private static ValueTask<AgentCommandResult> Dispatch(Harness h, string kind, string? configId = null) =>
        h.Server.Handler!(new AgentCommandEnvelope(kind, configId), Ct);

    [Fact]
    public async Task Registers_the_service_for_the_configured_host()
    {
        Harness h = await StartedAsync();

        Assert.Equal("post-01", h.Server.HostId);
        Assert.NotNull(h.Server.Handler);
    }

    [Fact]
    public async Task Stop_disposes_the_service_handle()
    {
        Harness h = await StartedAsync();

        await h.Service.StopAsync(Ct);

        Assert.True(h.Server.HandleDisposed);
    }

    [Fact]
    public async Task Launch_command_forwards_the_config_id()
    {
        Harness h = await StartedAsync();

        AgentCommandResult result = await Dispatch(h, AgentCommandKinds.Launch, "cfg1");

        Assert.True(result.Accepted);
        Assert.Equal("cfg1", Assert.Single(h.Controller.Launched));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Launch_without_a_config_id_is_rejected(string? configId)
    {
        Harness h = await StartedAsync();

        AgentCommandResult result = await Dispatch(h, AgentCommandKinds.Launch, configId);

        Assert.False(result.Accepted);
        Assert.Empty(h.Controller.Launched);
    }

    [Fact]
    public async Task Stop_command_stops_the_client()
    {
        Harness h = await StartedAsync();

        AgentCommandResult result = await Dispatch(h, AgentCommandKinds.Stop);

        Assert.True(result.Accepted);
        Assert.Equal(1, h.Controller.StopCount);
    }

    [Fact]
    public async Task Reconfigure_command_forwards_the_config_id()
    {
        Harness h = await StartedAsync();

        AgentCommandResult result = await Dispatch(h, AgentCommandKinds.Reconfigure, "cfg2");

        Assert.True(result.Accepted);
        Assert.Equal("cfg2", Assert.Single(h.Controller.Reconfigured));
    }

    [Fact]
    public async Task Reconfigure_without_a_config_id_is_rejected()
    {
        Harness h = await StartedAsync();

        AgentCommandResult result = await Dispatch(h, AgentCommandKinds.Reconfigure);

        Assert.False(result.Accepted);
        Assert.Empty(h.Controller.Reconfigured);
    }

    [Fact]
    public async Task Unknown_command_is_rejected()
    {
        Harness h = await StartedAsync();

        AgentCommandResult result = await Dispatch(h, "explode", "cfg1");

        Assert.False(result.Accepted);
        Assert.Contains("Unknown", result.Error);
    }

    [Fact]
    public async Task Command_kind_is_matched_exactly_so_wrong_case_is_rejected()
    {
        Harness h = await StartedAsync();

        // The wire constants are lowercase; matching is strict (Ordinal) to keep the protocol unambiguous.
        AgentCommandResult result = await Dispatch(h, "LAUNCH", "cfg1");

        Assert.False(result.Accepted);
        Assert.Empty(h.Controller.Launched);
    }

    [Fact]
    public async Task A_throwing_controller_becomes_a_declined_result_not_an_exception()
    {
        var controller = new FakeClientController { ThrowOn = new InvalidOperationException("kaboom") };
        Harness h = await StartedAsync(controller);

        AgentCommandResult result = await Dispatch(h, AgentCommandKinds.Stop);

        Assert.False(result.Accepted);
        Assert.Equal("kaboom", result.Error);
    }
}
