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
        return services.AddDasimRadioMessagingCore(() => RadioNatsOpts.ForUrl(url), configure);
    }

    /// <summary>
    /// Adds the messaging stack from <paramref name="options"/>, applying transport security (a NATS
    /// <c>.creds</c> file and/or TLS) from configuration. With no credentials and TLS disabled this is
    /// the same anonymous, plaintext connection as the URL overload — security is opt-in.
    /// </summary>
    public static IServiceCollection AddDasimRadioMessaging(
        this IServiceCollection services,
        RadioNatsOptions options,
        Func<NatsOpts, NatsOpts>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Url);
        return services.AddDasimRadioMessagingCore(() => RadioNatsOpts.Build(options), configure);
    }

    private static IServiceCollection AddDasimRadioMessagingCore(
        this IServiceCollection services,
        Func<NatsOpts> baseOpts,
        Func<NatsOpts, NatsOpts>? configure)
    {
        services.TryAddSingleton<INatsConnection>(_ =>
        {
            NatsOpts opts = baseOpts();
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
