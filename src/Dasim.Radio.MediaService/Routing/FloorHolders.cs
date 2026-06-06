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
public sealed class FloorControlHolders(FloorControlService floor) : IFloorHolders
{
    private readonly FloorControlService _floor = floor ?? throw new ArgumentNullException(nameof(floor));

    public FloorHolders Current()
    {
        var sources = new List<MixSource>();
        foreach (FloorSnapshot snapshot in _floor.ActiveFloors())
        {
            if (snapshot.Holder is { } holder && snapshot.HolderPriority is { } priority)
            {
                sources.Add(new MixSource(holder, snapshot.Net, priority));
            }
        }

        return FloorHolders.From(sources);
    }
}
