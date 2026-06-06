using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Floor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dasim.Radio.MediaService.Routing;

/// <summary>DI registration for the media service's data-plane routing.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the per-listener routing: the force-tree provider (kept in step with the
    /// <c>force_tree</c> bucket), the combine policy, the floor-holder source, and the media-router
    /// host. Also REPLACES the interim client-trusting floor priority resolver with the authoritative
    /// force-tree one, so call this after <c>AddFloorAuthority</c> (which it depends on for
    /// <see cref="FloorControlService"/>) and after <c>AddDasimRadioMessaging</c>.
    /// </summary>
    public static IServiceCollection AddMediaRouting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Default combine policy: a superior cuts through. Swap to AdditiveMixPolicy to sum every
        // active net the listener is on instead.
        services.TryAddSingleton<IMixPolicy, PriorityOverrideMixPolicy>();

        services.TryAddSingleton<ForceTreeProvider>();
        services.TryAddSingleton<IForceTreeProvider>(sp => sp.GetRequiredService<ForceTreeProvider>());
        services.AddHostedService(sp => sp.GetRequiredService<ForceTreeProvider>());

        // Authoritative force-tree priority replaces the interim RequestPriorityResolver registered by
        // AddFloorAuthority: a client can no longer inflate its rank to pre-empt a superior.
        services.RemoveAll<IFloorPriorityResolver>();
        services.AddSingleton<IFloorPriorityResolver, ForceTreePriorityResolver>();

        services.TryAddSingleton<IFloorHolders, FloorControlHolders>();
        services.TryAddSingleton<MediaRouter>();
        services.AddHostedService<MediaRouterService>();

        return services;
    }
}
