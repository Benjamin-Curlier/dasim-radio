using Dasim.Radio.Manager.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dasim.Radio.Manager.Core.Tests;

public sealed class ManagerOptionsValidatorTests
{
    [Fact]
    public void Accepts_a_positive_stale_window()
    {
        ValidateOptionsResult result = new ManagerOptionsValidator()
            .Validate(null, new ManagerOptions { PresenceStaleAfter = TimeSpan.FromSeconds(15) });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Rejects_a_non_positive_stale_window()
    {
        ValidateOptionsResult result = new ManagerOptionsValidator()
            .Validate(null, new ManagerOptions { PresenceStaleAfter = TimeSpan.Zero });

        Assert.True(result.Failed);
    }
}

public sealed class ServiceCollectionExtensionsTests
{
    private static IServiceCollection Configured()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Manager:PresenceStaleAfter"] = "00:00:20" })
            .Build();

        var services = new ServiceCollection();
        services.AddDasimRadioManagerCore(config);
        return services;
    }

    [Fact]
    public void Registers_all_core_services()
    {
        IServiceCollection services = Configured();

        Assert.Contains(services, d => d.ServiceType == typeof(IClientConfigService));
        Assert.Contains(services, d => d.ServiceType == typeof(IForceTreeService));
        Assert.Contains(services, d => d.ServiceType == typeof(IPostDirectoryService));
        Assert.Contains(services, d => d.ServiceType == typeof(IPostControlService));
        Assert.Contains(services, d => d.ServiceType == typeof(IDegradeControlService));
        Assert.Contains(services, d => d.ServiceType == typeof(TimeProvider));
    }

    [Fact]
    public void Binds_and_validates_options()
    {
        using ServiceProvider provider = Configured().BuildServiceProvider();

        ManagerOptions options = provider.GetRequiredService<IOptions<ManagerOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(20), options.PresenceStaleAfter);
        Assert.Contains(
            provider.GetServices<IValidateOptions<ManagerOptions>>(), v => v is ManagerOptionsValidator);
    }
}
