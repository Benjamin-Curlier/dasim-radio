namespace Dasim.Radio.Core;

/// <summary>
/// A speaker currently holding the floor on a net — a candidate source for a listener's mix. The
/// floor guarantees at most one of these per net.
/// </summary>
public readonly record struct MixSource(ParticipantId Speaker, NetId Net, Priority Priority);

/// <summary>
/// Point-in-time view of which participant (if any) holds each net's floor, fed to the
/// <see cref="MixPlanner"/>. Immutable; rebuild or replace it per mixing tick from the floor state.
/// </summary>
public sealed class FloorHolders
{
    private readonly IReadOnlyDictionary<NetId, MixSource> _byNet;

    public FloorHolders(IReadOnlyDictionary<NetId, MixSource> byNet)
    {
        ArgumentNullException.ThrowIfNull(byNet);
        _byNet = byNet;
    }

    /// <summary>No net is held — every listener's plan is silent.</summary>
    public static FloorHolders Empty { get; } = new(new Dictionary<NetId, MixSource>());

    /// <summary>The nets that currently have a holder.</summary>
    public IReadOnlyCollection<NetId> ActiveNets => (IReadOnlyCollection<NetId>)_byNet.Keys;

    /// <summary>Builds a snapshot from the active holders.</summary>
    public static FloorHolders From(IEnumerable<MixSource> holders)
    {
        ArgumentNullException.ThrowIfNull(holders);
        var byNet = new Dictionary<NetId, MixSource>();
        foreach (MixSource holder in holders)
        {
            byNet[holder.Net] = holder;
        }

        return new FloorHolders(byNet);
    }

    /// <summary>Returns the holder of a net, if any.</summary>
    public bool TryGetHolder(NetId net, out MixSource holder) => _byNet.TryGetValue(net, out holder);
}

/// <summary>What a single listener should hear this tick: the sources to combine into their mix.</summary>
public sealed record MixPlan(ParticipantId Listener, IReadOnlyList<MixSource> Sources)
{
    /// <summary>The listener hears nothing this tick (no source to encode — emit silence or skip).</summary>
    public bool IsSilent => Sources.Count == 0;
}

/// <summary>
/// Decides which of a listener's candidate sources reach their mix when more than one of their nets
/// is active at once. The floor guarantees ≤1 holder per net, so the candidate count never exceeds
/// the number of nets the listener is on (≤2 in a clean tree). This is the only place the
/// additive-vs-override behaviour differs; both implementations are pure and stateless.
/// </summary>
public interface IMixPolicy
{
    /// <summary>Picks the sources to combine from a listener's active candidates (never null; may be empty).</summary>
    IReadOnlyList<MixSource> Select(IReadOnlyList<MixSource> candidates);
}

/// <summary>Hear everyone at once: all active sources are summed into the listener's mix (nothing is lost).</summary>
public sealed class AdditiveMixPolicy : IMixPolicy
{
    public IReadOnlyList<MixSource> Select(IReadOnlyList<MixSource> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates;
    }
}

/// <summary>
/// A superior cuts through: the listener hears only the highest-priority active source, so a
/// transmission on a higher net suppresses lower chatter for that listener. Ties (equal priority on
/// two of the listener's nets) break deterministically on the speaker id so the choice is stable.
/// </summary>
public sealed class PriorityOverrideMixPolicy : IMixPolicy
{
    public IReadOnlyList<MixSource> Select(IReadOnlyList<MixSource> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count <= 1)
        {
            return candidates;
        }

        MixSource winner = candidates[0];
        for (int i = 1; i < candidates.Count; i++)
        {
            MixSource source = candidates[i];
            if (source.Priority > winner.Priority ||
                (source.Priority.CompareTo(winner.Priority) == 0 &&
                 string.CompareOrdinal(source.Speaker.Value, winner.Speaker.Value) < 0))
            {
                winner = source;
            }
        }

        return [winner];
    }
}

/// <summary>
/// Computes, per listener, what they should hear: gathers the holders of the nets the listener
/// belongs to (excluding the listener's own transmission) and applies an <see cref="IMixPolicy"/>.
/// Pure and deterministic — it takes the floor state as input rather than reading it — so the whole
/// routing model is unit-testable without a broker or audio.
/// </summary>
public sealed class MixPlanner
{
    private readonly NetTopology _topology;

    public MixPlanner(NetTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        _topology = topology;
    }

    /// <summary>Computes the mix plan for one listener given the current floor holders and combine policy.</summary>
    public MixPlan PlanFor(ParticipantId listener, FloorHolders holders, IMixPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(holders);
        ArgumentNullException.ThrowIfNull(policy);

        NetMembership membership = _topology.MembershipOf(listener);
        if (membership.Nets.Count == 0)
        {
            return new MixPlan(listener, []);
        }

        var candidates = new List<MixSource>(membership.Nets.Count);
        foreach (NetId net in membership.Nets)
        {
            // ≤1 holder per net (floor guarantee); a listener never hears their own transmission.
            if (holders.TryGetHolder(net, out MixSource holder) && holder.Speaker != listener)
            {
                candidates.Add(holder);
            }
        }

        IReadOnlyList<MixSource> selected = candidates.Count == 0 ? [] : policy.Select(candidates);
        return new MixPlan(listener, selected);
    }
}
