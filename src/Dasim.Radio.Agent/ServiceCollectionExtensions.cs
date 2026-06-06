using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Dasim.Radio.Agent;

/// <summary>DI registration for the host agent.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the agent: validated <see cref="AgentOptions"/>, the client controller and its process
    /// runner, and the presence-heartbeat and command-service hosts. Requires the messaging stack
    /// (<c>AddDasimRadioMessaging</c>) for <c>IPresenceChannel</c>/<c>IControlPlaneStore</c>/
    /// <c>IAgentCommandServer</c>.
    /// </summary>
    public static IServiceCollection AddDasimRadioAgent(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AgentOptions>()
            .Bind(configuration.GetSection(AgentOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AgentOptions>, AgentOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IProcessRunner, SystemProcessRunner>();
        services.TryAddSingleton<IClientController, ProcessClientController>();

        services.AddHostedService<PresenceHeartbeatService>();
        services.AddHostedService<AgentCommandHostedService>();

        return services;
    }
}
