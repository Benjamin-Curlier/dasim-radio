using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Dasim.Radio.Manager.Core;

/// <summary>DI registration for the manager's core services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers validated <see cref="ManagerOptions"/> and the manager's orchestration services (config CRUD,
    /// force-tree import, post directory, post control, degrade control). Requires the messaging stack
    /// (<c>AddDasimRadioMessaging</c>) for <c>IControlPlaneStore</c>/<c>IAgentCommandClient</c>/
    /// <c>IPresenceChannel</c>/<c>IDegradeChannel</c>.
    /// </summary>
    public static IServiceCollection AddDasimRadioManagerCore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ManagerOptions>()
            .Bind(configuration.GetSection(ManagerOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ManagerOptions>, ManagerOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IClientConfigService, ClientConfigService>();
        services.TryAddSingleton<IForceTreeService, ForceTreeService>();
        services.TryAddSingleton<IPostDirectoryService, PostDirectoryService>();
        services.TryAddSingleton<IPostControlService, PostControlService>();
        services.TryAddSingleton<IDegradeControlService, DegradeControlService>();

        return services;
    }
}
