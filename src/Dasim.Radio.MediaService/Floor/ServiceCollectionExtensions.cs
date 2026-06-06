using Dasim.Radio.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dasim.Radio.MediaService.Floor;

/// <summary>DI registration for the media service's floor authority.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the floor authority: the authoritative <see cref="FloorControlService"/>, the
    /// priority and state-writer seams, and the <see cref="FloorAuthorityService"/> host. Requires
    /// the messaging stack (<c>AddDasimRadioMessaging</c>) for <c>IFloorSignal</c>/
    /// <c>IControlPlaneStore</c>, and logging to be registered.
    /// </summary>
    public static IServiceCollection AddFloorAuthority(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<FloorControlService>();
        services.TryAddSingleton<IFloorPriorityResolver, RequestPriorityResolver>();
        services.TryAddSingleton<IFloorStateWriter, ControlPlaneFloorStateWriter>();
        services.TryAddSingleton<FloorArbiter>();
        services.AddHostedService<FloorAuthorityService>();

        return services;
    }
}
