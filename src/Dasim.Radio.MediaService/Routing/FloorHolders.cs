using Dasim.Radio.Core;

namespace Dasim.Radio.MediaService.Routing;

/// <summary>Supplies the current floor holders as a <see cref="Core.FloorHolders"/> routing snapshot.</summary>
public interface IFloorHolders
{
    /// <summary>The live holders this instant (one entry per held net).</summary>
    FloorHolders Current();
}

/// <summary>
/// Reads the live holders straight from the in-process authoritative <see cref="FloorControlService"/>
/// — the same instance the floor authority arbitrates with — so the router and the floor always agree.
/// </summary>
/// <remarks>
/// The holder set changes only on a grant/pre-emption/release, not every 20 ms tick, so the snapshot
/// is cached and rebuilt only when <see cref="FloorControlService.Version"/> moves. The router drives
/// this from a single consumer, so the cache needs no synchronisation. The version is read <em>before</em>
/// the rebuild so a concurrent floor change can never be cached under a stale version (it only ever
/// triggers one extra, harmless rebuild next tick).
/// </remarks>
public sealed class FloorControlHolders(FloorControlService floor) : IFloorHolders
{
    private readonly FloorControlService _floor = floor ?? throw new ArgumentNullException(nameof(floor));

    private FloorHolders _cached = FloorHolders.Empty;
    private long _cachedVersion = -1;

    public FloorHolders Current()
    {
        long version = _floor.Version;
        if (version == _cachedVersion)
        {
            return _cached;
        }

        var sources = new List<MixSource>();
        foreach (FloorSnapshot snapshot in _floor.ActiveFloors())
        {
            if (snapshot.Holder is { } holder && snapshot.HolderPriority is { } priority)
            {
                sources.Add(new MixSource(holder, snapshot.Net, priority));
            }
        }

        _cached = FloorHolders.From(sources);
        _cachedVersion = version;
        return _cached;
    }
}
