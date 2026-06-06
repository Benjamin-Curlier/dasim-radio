using Dasim.Radio.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dasim.Radio.Client.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    private static IServiceCollection Configured()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Client:ClientId"] = "c9",
                ["Client:ParticipantId"] = "p9",
                ["Client:OwnNetId"] = "bravo",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddDasimRadioClient(config);
        return services;
    }

    [Fact]
    public void Registers_the_engine_and_default_hotkey()
    {
        IServiceCollection services = Configured();

        Assert.Contains(services, d => d.ServiceType == typeof(RadioClientEngine));
        Assert.Contains(services, d =>
            d.ServiceType == typeof(IPushToTalkHotkey) && d.ImplementationType == typeof(ManualPushToTalk));
    }

    [Fact]
    public void Binds_and_validates_options()
    {
        using ServiceProvider provider = Configured().BuildServiceProvider();

        ClientOptions options = provider.GetRequiredService<IOptions<ClientOptions>>().Value;

        Assert.Equal("c9", options.ClientId);
        Assert.Equal("bravo", options.OwnNetId);
        Assert.Contains(
            provider.GetServices<IValidateOptions<ClientOptions>>(), v => v is ClientOptionsValidator);
    }
}
