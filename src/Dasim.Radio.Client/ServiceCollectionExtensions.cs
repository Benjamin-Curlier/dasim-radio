using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Dasim.Radio.Client;

/// <summary>DI registration for the radio client engine.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers validated <see cref="ClientOptions"/>, the default on-screen <see cref="IPushToTalkHotkey"/>
    /// (<see cref="ManualPushToTalk"/>), and the <see cref="RadioClientEngine"/>. The host must also
    /// register the messaging stack (<c>AddDasimRadioMessaging</c>) for <c>IAudioBus</c>/<c>IFloorSignal</c>,
    /// the Concentus codec factories, and an <c>IAudioCaptureDevice</c>/<c>IAudioPlaybackDevice</c> — and
    /// may override the hotkey with a platform global-PTT provider.
    /// </summary>
    public static IServiceCollection AddDasimRadioClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ClientOptions>()
            .Bind(configuration.GetSection(ClientOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ClientOptions>, ClientOptionsValidator>());

        services.TryAddSingleton<IPushToTalkHotkey, ManualPushToTalk>();
        services.TryAddSingleton<RadioClientEngine>();

        return services;
    }
}
