using Dasim.Radio.Messaging.Agent;
using Dasim.Radio.Messaging.Audio;
using Dasim.Radio.Messaging.Degrade;
using Dasim.Radio.Messaging.Floor;
using Dasim.Radio.Messaging.KeyValue;
using Dasim.Radio.Messaging.Presence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NATS.Client.Core;

namespace Dasim.Radio.Messaging;

/// <summary>Registers the Dasim.Radio messaging stack on a host's service collection.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds a shared <see cref="INatsConnection"/> (wired with the Dasim.Radio serializer
    /// registry) and every messaging wrapper. <paramref name="configure"/> may layer TLS/auth on
    /// the options as long as it preserves the registry.
    /// </summary>
    public static IServiceCollection AddDasimRadioMessaging(
        this IServiceCollection services,
        string url,
        Func<NatsOpts, NatsOpts>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        services.TryAddSingleton<INatsConnection>(_ =>
        {
            NatsOpts opts = RadioNatsOpts.ForUrl(url);
            if (configure is not null)
            {
                opts = configure(opts);
            }

            return new NatsConnection(opts);
        });

        services.TryAddSingleton<IAudioBus, NatsAudioBus>();
        services.TryAddSingleton<IControlPlaneStore, NatsControlPlaneStore>();
        services.TryAddSingleton<IFloorSignal, NatsFloorSignal>();
        services.TryAddSingleton<IPresenceChannel, NatsPresenceChannel>();
        services.TryAddSingleton<IDegradeChannel, NatsDegradeChannel>();
        services.TryAddSingleton<IAgentCommandClient, NatsAgentCommandClient>();
        services.TryAddSingleton<IAgentCommandServer, NatsAgentCommandServer>();

        return services;
    }
}
