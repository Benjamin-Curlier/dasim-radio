using Dasim.Radio.Agent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dasim.Radio.Agent.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    private static IServiceCollection Configured()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:HostId"] = "post-99",
                ["Agent:HeartbeatInterval"] = "00:00:03",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDasimRadioAgent(config);
        return services;
    }

    [Fact]
    public void Registers_the_controller_runner_and_clock()
    {
        IServiceCollection services = Configured();

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IProcessRunner) && d.ImplementationType == typeof(SystemProcessRunner));
        Assert.Contains(services, d =>
            d.ServiceType == typeof(IClientController) && d.ImplementationType == typeof(ProcessClientController));
        Assert.Contains(services, d => d.ServiceType == typeof(TimeProvider));
    }

    [Fact]
    public void Registers_both_hosted_services()
    {
        IServiceCollection services = Configured();

        List<Type?> hosted = [.. services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)];

        Assert.Contains(typeof(PresenceHeartbeatService), hosted);
        Assert.Contains(typeof(AgentCommandHostedService), hosted);
    }

    [Fact]
    public void Binds_options_from_configuration()
    {
        using ServiceProvider provider = Configured().BuildServiceProvider();

        AgentOptions options = provider.GetRequiredService<IOptions<AgentOptions>>().Value;

        Assert.Equal("post-99", options.HostId);
        Assert.Equal(TimeSpan.FromSeconds(3), options.HeartbeatInterval);
    }

    [Fact]
    public void Registers_the_options_validator()
    {
        using ServiceProvider provider = Configured().BuildServiceProvider();

        IEnumerable<IValidateOptions<AgentOptions>> validators =
            provider.GetServices<IValidateOptions<AgentOptions>>();

        Assert.Contains(validators, v => v is AgentOptionsValidator);
    }
}
