using Dasim.Radio.Agent;
using Dasim.Radio.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dasim.Radio.Agent.Tests;

public sealed class ProcessClientControllerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ProcessClientController Controller(FakeProcessRunner runner, AgentOptions? options = null) =>
        new(
            runner,
            Options.Create(options ?? new AgentOptions { ClientExecutablePath = "client.exe" }),
            NullLogger<ProcessClientController>.Instance);

    [Fact]
    public async Task Launch_when_idle_starts_the_client()
    {
        var runner = new FakeProcessRunner();
        ProcessClientController controller = Controller(runner);

        AgentCommandResult result = await controller.LaunchAsync("cfg1", Ct);

        Assert.True(result.Accepted);
        FakeProcessHandle started = Assert.Single(runner.Started);
        Assert.Equal("client.exe", started.ExecutablePath);
        Assert.Equal("cfg1", started.ConfigId);
        Assert.True(controller.IsRunning);
        Assert.Equal("cfg1", controller.CurrentConfigId);
    }

    [Fact]
    public async Task Launch_with_unconfigured_executable_is_rejected()
    {
        var runner = new FakeProcessRunner();
        ProcessClientController controller = Controller(runner, new AgentOptions { ClientExecutablePath = "" });

        AgentCommandResult result = await controller.LaunchAsync("cfg1", Ct);

        Assert.False(result.Accepted);
        Assert.Empty(runner.Started);
    }

    [Fact]
    public async Task Launch_while_running_is_rejected_by_default()
    {
        var runner = new FakeProcessRunner();
        ProcessClientController controller = Controller(runner);
        await controller.LaunchAsync("cfg1", Ct);

        AgentCommandResult result = await controller.LaunchAsync("cfg2", Ct);

        Assert.False(result.Accepted);
        Assert.Single(runner.Started); // no second start
        Assert.Equal("cfg1", controller.CurrentConfigId);
    }

    [Fact]
    public async Task Launch_while_running_replaces_when_allowed()
    {
        var runner = new FakeProcessRunner();
        ProcessClientController controller = Controller(
            runner, new AgentOptions { ClientExecutablePath = "client.exe", AllowReplaceRunningClient = true });
        await controller.LaunchAsync("cfg1", Ct);

        AgentCommandResult result = await controller.LaunchAsync("cfg2", Ct);

        Assert.True(result.Accepted);
        Assert.Equal(2, runner.Started.Count);
        Assert.Equal(1, runner.Started[0].KillCount); // old client killed
        Assert.True(runner.Started[0].Disposed);
        Assert.Equal("cfg2", controller.CurrentConfigId);
    }

    [Fact]
    public async Task Stop_when_running_kills_and_clears()
    {
        var runner = new FakeProcessRunner();
        ProcessClientController controller = Controller(runner);
        await controller.LaunchAsync("cfg1", Ct);

        AgentCommandResult result = await controller.StopAsync(Ct);

        Assert.True(result.Accepted);
        Assert.Equal(1, runner.Started[0].KillCount);
        Assert.True(runner.Started[0].Disposed);
        Assert.False(controller.IsRunning);
        Assert.Null(controller.CurrentConfigId);
    }

    [Fact]
    public async Task Stop_when_idle_is_a_noop()
    {
        var runner = new FakeProcessRunner();
        ProcessClientController controller = Controller(runner);

        AgentCommandResult result = await controller.StopAsync(Ct);

        Assert.True(result.Accepted);
        Assert.Empty(runner.Started);
    }

    [Fact]
    public async Task Reconfigure_stops_then_relaunches()
    {
        var runner = new FakeProcessRunner();
        ProcessClientController controller = Controller(runner);
        await controller.LaunchAsync("cfg1", Ct);

        AgentCommandResult result = await controller.ReconfigureAsync("cfg2", Ct);

        Assert.True(result.Accepted);
        Assert.Equal(2, runner.Started.Count);
        Assert.Equal(1, runner.Started[0].KillCount);
        Assert.Equal("cfg2", runner.Started[1].ConfigId);
        Assert.Equal("cfg2", controller.CurrentConfigId);
    }

    [Fact]
    public async Task Reconfigure_that_fails_to_relaunch_ends_not_running()
    {
        var runner = new FakeProcessRunner();
        ProcessClientController controller = Controller(runner);
        await controller.LaunchAsync("cfg1", Ct);

        runner.StartException = new InvalidOperationException("boom");
        AgentCommandResult result = await controller.ReconfigureAsync("cfg2", Ct);

        Assert.False(result.Accepted);
        Assert.False(controller.IsRunning);
        Assert.Null(controller.CurrentConfigId);
        Assert.Equal(1, runner.Started[0].KillCount); // old client still stopped
    }

    [Fact]
    public async Task Client_that_exits_on_its_own_is_reported_not_running()
    {
        var runner = new FakeProcessRunner();
        ProcessClientController controller = Controller(runner);
        await controller.LaunchAsync("cfg1", Ct);

        runner.Started[0].HasExited = true; // client crashed without an explicit stop

        Assert.False(controller.IsRunning);
        Assert.Null(controller.CurrentConfigId);
    }

    [Fact]
    public async Task Dispose_releases_the_handle()
    {
        var runner = new FakeProcessRunner();
        ProcessClientController controller = Controller(runner);
        await controller.LaunchAsync("cfg1", Ct);

        controller.Dispose();

        Assert.True(runner.Started[0].Disposed);
        Assert.False(controller.IsRunning);
    }
}
