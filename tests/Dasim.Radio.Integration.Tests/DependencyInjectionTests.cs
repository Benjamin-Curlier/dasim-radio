using Dasim.Radio.Messaging;
using Dasim.Radio.Messaging.Agent;
using Dasim.Radio.Messaging.Audio;
using Dasim.Radio.Messaging.Degrade;
using Dasim.Radio.Messaging.Floor;
using Dasim.Radio.Messaging.KeyValue;
using Dasim.Radio.Messaging.Presence;
using Dasim.Radio.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using Xunit;

namespace Dasim.Radio.Integration.Tests;

/// <summary>Wiring tests that need no broker (they never connect).</summary>
public sealed class DependencyInjectionTests
{
    [Fact]
    public async Task AddDasimRadioMessaging_registers_all_wrappers()
    {
        var services = new ServiceCollection();
        services.AddDasimRadioMessaging("nats://localhost:4222");
        await using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<NatsConnection>(provider.GetRequiredService<INatsConnection>());
        Assert.IsType<NatsAudioBus>(provider.GetRequiredService<IAudioBus>());
        Assert.IsType<NatsControlPlaneStore>(provider.GetRequiredService<IControlPlaneStore>());
        Assert.IsType<NatsFloorSignal>(provider.GetRequiredService<IFloorSignal>());
        Assert.IsType<NatsPresenceChannel>(provider.GetRequiredService<IPresenceChannel>());
        Assert.IsType<NatsDegradeChannel>(provider.GetRequiredService<IDegradeChannel>());
        Assert.IsType<NatsAgentCommandClient>(provider.GetRequiredService<IAgentCommandClient>());
        Assert.IsType<NatsAgentCommandServer>(provider.GetRequiredService<IAgentCommandServer>());
    }

    [Fact]
    public void RadioNatsOpts_pins_the_radio_serializer_registry()
    {
        NatsOpts opts = RadioNatsOpts.ForUrl("nats://localhost:4222");

        Assert.IsType<RadioSerializerRegistry>(opts.SerializerRegistry);
        Assert.Equal("nats://localhost:4222", opts.Url);
        Assert.Equal(RadioNatsOpts.DefaultName, opts.Name);
    }

    [Fact]
    public void RadioNatsOpts_honours_a_custom_client_name()
    {
        NatsOpts opts = RadioNatsOpts.ForUrl("nats://localhost:4222", "media-service");

        Assert.Equal("media-service", opts.Name);
        Assert.IsType<RadioSerializerRegistry>(opts.SerializerRegistry);
    }

    [Fact]
    public async Task AddDasimRadioMessaging_applies_the_configure_callback()
    {
        var services = new ServiceCollection();
        var configured = false;

        services.AddDasimRadioMessaging(
            "nats://localhost:4222",
            opts =>
            {
                configured = true;
                return opts with { Name = "agent-host" };
            });

        await using ServiceProvider provider = services.BuildServiceProvider();
        var connection = (NatsConnection)provider.GetRequiredService<INatsConnection>();

        Assert.True(configured);
        Assert.Equal("agent-host", connection.Opts.Name);
        // The callback must not be able to drop the registry by accident.
        Assert.IsType<RadioSerializerRegistry>(connection.Opts.SerializerRegistry);
    }
}
