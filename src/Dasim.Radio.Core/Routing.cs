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

/// <summary>Helpers for choosing among a listener's candidate sources.</summary>
public static class MixSources
{
    /// <summary>
    /// The dominant source: the highest priority, ties broken on the speaker id (ordinal). This is
    /// both what <see cref="PriorityOverrideMixPolicy"/> keeps and the "trigger" the media router uses
    /// to emit a listener's mix exactly once per cycle. Throws if <paramref name="sources"/> is empty.
    /// </summary>
    public static MixSource Highest(IReadOnlyList<MixSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            throw new ArgumentException("Cannot pick the highest of an empty source set.", nameof(sources));
        }

        MixSource winner = sources[0];
        for (int i = 1; i < sources.Count; i++)
        {
            MixSource source = sources[i];
            if (source.Priority > winner.Priority ||
                (source.Priority.CompareTo(winner.Priority) == 0 &&
                 string.CompareOrdinal(source.Speaker.Value, winner.Speaker.Value) < 0))
            {
                winner = source;
            }
        }

        return winner;
    }
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

    /// <summary>
    /// The allocation-free hot-path form of <see cref="Select"/>: reduces a caller-owned candidate list
    /// to the sources to combine, in place, so the per-frame router never allocates a result list. Must
    /// be equivalent to <see cref="Select"/> for the same input.
    /// </summary>
    void SelectInPlace(List<MixSource> candidates);
}

/// <summary>Hear everyone at once: all active sources are summed into the listener's mix (nothing is lost).</summary>
public sealed class AdditiveMixPolicy : IMixPolicy
{
    public IReadOnlyList<MixSource> Select(IReadOnlyList<MixSource> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates;
    }

    /// <summary>Additive keeps every candidate, so the list is already the answer — nothing to do.</summary>
    public void SelectInPlace(List<MixSource> candidates) => ArgumentNullException.ThrowIfNull(candidates);
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
        return candidates.Count <= 1 ? candidates : [MixSources.Highest(candidates)];
    }

    public void SelectInPlace(List<MixSource> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count <= 1)
        {
            return;
        }

        MixSource winner = MixSources.Highest(candidates);
        candidates.Clear();
        candidates.Add(winner);
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

    /// <summary>
    /// The allocation-free form of <see cref="PlanFor"/> for the per-frame router: fills
    /// <paramref name="candidates"/> (a caller-owned, reused buffer) with the listener's selected
    /// sources instead of allocating a <see cref="MixPlan"/> and a fresh source list. The buffer is
    /// cleared first; on return it holds the same sources <see cref="PlanFor"/> would have produced
    /// (empty when the listener hears nothing).
    /// </summary>
    public void PlanInto(ParticipantId listener, FloorHolders holders, IMixPolicy policy, List<MixSource> candidates)
    {
        ArgumentNullException.ThrowIfNull(holders);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(candidates);

        candidates.Clear();
        NetMembership membership = _topology.MembershipOf(listener);

        // Index, don't foreach: a foreach over the IReadOnlyList-typed Nets boxes an enumerator, and
        // this runs per listener per frame on the router's hot path.
        IReadOnlyList<NetId> nets = membership.Nets;
        for (int i = 0; i < nets.Count; i++)
        {
            // ≤1 holder per net (floor guarantee); a listener never hears their own transmission.
            if (holders.TryGetHolder(nets[i], out MixSource holder) && holder.Speaker != listener)
            {
                candidates.Add(holder);
            }
        }

        if (candidates.Count > 0)
        {
            policy.SelectInPlace(candidates);
        }
    }
}
