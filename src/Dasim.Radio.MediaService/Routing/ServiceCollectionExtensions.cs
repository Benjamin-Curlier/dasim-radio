using Dasim.Radio.Audio.Opus;
using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Degrade;
using Dasim.Radio.MediaService.Floor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dasim.Radio.MediaService.Routing;

/// <summary>How a listener's concurrently-active nets are combined into their mix.</summary>
public enum MixCombinePolicy
{
    /// <summary>A superior cuts through: the listener hears only the highest-priority active net.</summary>
    Override,

    /// <summary>The listener hears every active net they are on, summed together.</summary>
    Additive,
}

/// <summary>DI registration for the media service's data-plane routing.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the per-listener routing: the force-tree provider (kept in step with the
    /// <c>force_tree</c> bucket), the chosen combine policy, the floor-holder source, the codec +
    /// degradation pipeline, and the media-router host. Also REPLACES the interim client-trusting floor
    /// priority resolver with the authoritative force-tree one, so call this after
    /// <c>AddFloorAuthority</c> (which it depends on for <see cref="FloorControlService"/>) and after
    /// <c>AddDasimRadioMessaging</c>.
    /// </summary>
    public static IServiceCollection AddMediaRouting(
        this IServiceCollection services, MixCombinePolicy combinePolicy = MixCombinePolicy.Override)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (combinePolicy == MixCombinePolicy.Additive)
        {
            services.TryAddSingleton<IMixPolicy, AdditiveMixPolicy>();
        }
        else
        {
            services.TryAddSingleton<IMixPolicy, PriorityOverrideMixPolicy>();
        }

        services.TryAddSingleton<ForceTreeProvider>();
        services.TryAddSingleton<IForceTreeProvider>(sp => sp.GetRequiredService<ForceTreeProvider>());
        services.AddHostedService(sp => sp.GetRequiredService<ForceTreeProvider>());

        // Authoritative force-tree priority replaces the interim RequestPriorityResolver registered by
        // AddFloorAuthority: a client can no longer inflate its rank to pre-empt a superior.
        services.RemoveAll<IFloorPriorityResolver>();
        services.AddSingleton<IFloorPriorityResolver, ForceTreePriorityResolver>();

        services.TryAddSingleton<IFloorHolders, FloorControlHolders>();
        services.TryAddSingleton<MediaRouter>();

        // Per-listener degradation: native-libopus codec, the cmd.degrade registry + host, the clarity
        // DSP, and the renderer that decodes → degrades → re-encodes (and passes undegraded through).
        services.AddOpusSharpCodec();
        services.TryAddSingleton<IDegradeRegistry, DegradeRegistry>();
        services.AddHostedService<DegradeCommandService>();
        services.TryAddSingleton<ClarityProcessor>();
        services.TryAddSingleton<MixRenderer>();

        services.AddHostedService<MediaRouterService>();

        return services;
    }
}
