using Dasim.Radio.Core;

namespace Dasim.Radio.MediaService.Routing;

/// <summary>
/// An immutable snapshot of everything the media service derives from one <c>force_tree</c> version:
/// the domain tree (for priority lookup), the subtree-net <see cref="NetTopology"/>, and a
/// <see cref="MixPlanner"/> bound to it. Swapped atomically by the <see cref="IForceTreeProvider"/>
/// when a new version is imported, so readers never see a half-built topology.
/// </summary>
public sealed record ForceRouting(int Version, ForceTree? Tree, NetTopology Topology, MixPlanner Planner)
{
    /// <summary>The state before any force tree is loaded: no nets, so nothing routes.</summary>
    public static ForceRouting Empty { get; } = Create(0, null, NetTopology.Empty);

    /// <summary>Builds a routing snapshot, deriving the planner from the topology.</summary>
    public static ForceRouting Create(int version, ForceTree? tree, NetTopology topology) =>
        new(version, tree, topology, new MixPlanner(topology));
}

/// <summary>Supplies the current <see cref="ForceRouting"/>, kept up to date with the <c>force_tree</c> bucket.</summary>
public interface IForceTreeProvider
{
    /// <summary>The current routing snapshot (never null; <see cref="ForceRouting.Empty"/> until a tree loads).</summary>
    ForceRouting Current { get; }
}

/// <summary>
/// A fixed <see cref="IForceTreeProvider"/> that always returns the same snapshot. Registered as the
/// deny-by-default (<see cref="ForceRouting.Empty"/>) provider by <c>AddFloorAuthority</c> so the floor
/// authority composes and runs safely on its own — with no force tree, every floor request is denied —
/// until <c>AddMediaRouting</c> replaces it with the live, KV-watching provider.
/// </summary>
public sealed class StaticForceTreeProvider(ForceRouting routing) : IForceTreeProvider
{
    public ForceRouting Current { get; } = routing;
}
