using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Routing;
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
        // The arbiter authorizes every request/release against the force tree. Default to a deny-by-default
        // empty provider so the floor authority can stand alone safely; AddMediaRouting replaces it with the
        // live, KV-watching provider.
        services.TryAddSingleton<IForceTreeProvider>(_ => new StaticForceTreeProvider(ForceRouting.Empty));
        services.TryAddSingleton<FloorArbiter>();
        services.AddHostedService<FloorAuthorityService>();

        return services;
    }
}
