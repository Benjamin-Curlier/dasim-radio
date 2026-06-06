using Dasim.Radio.Core;

namespace Dasim.Radio.MediaService.Routing;

/// <summary>
/// What one listener should be sent when their trigger source produces a frame: the listener and the
/// full set of sources to combine for them (one source under the override policy, possibly several
/// under additive).
/// </summary>
public readonly record struct MixDelivery(ParticipantId Listener, IReadOnlyList<MixSource> Sources);

/// <summary>
/// Decides, for a captured frame from one speaker, which listeners' mixes that frame should drive.
/// A listener's mix is emitted exactly once per cycle — on the arrival of its <em>trigger</em> (the
/// highest-priority source in its plan) — so a multi-source additive listener is not emitted twice.
/// A frame from a non-trigger source updates that source's buffer (in the renderer) but drives no
/// output here. Under the override policy every plan has a single source, which is its own trigger, so
/// this reduces to "the listeners who currently hear this speaker".
/// </summary>
public sealed class MediaRouter
{
    private readonly IForceTreeProvider _force;
    private readonly IFloorHolders _holders;
    private readonly IMixPolicy _policy;

    public MediaRouter(IForceTreeProvider force, IFloorHolders holders, IMixPolicy policy)
    {
        _force = force ?? throw new ArgumentNullException(nameof(force));
        _holders = holders ?? throw new ArgumentNullException(nameof(holders));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <summary>The mixes that <paramref name="speaker"/>'s current frame should drive (empty if it holds no net).</summary>
    public IReadOnlyList<MixDelivery> Deliveries(ParticipantId speaker)
    {
        ForceRouting routing = _force.Current;
        NetMembership membership = routing.Topology.MembershipOf(speaker);
        if (membership.Nets.Count == 0)
        {
            return [];
        }

        FloorHolders holders = _holders.Current();

        List<NetId>? heldNets = null;
        foreach (NetId net in membership.Nets)
        {
            if (holders.TryGetHolder(net, out MixSource holder) && holder.Speaker == speaker)
            {
                (heldNets ??= []).Add(net);
            }
        }

        if (heldNets is null)
        {
            // The speaker is transmitting without the floor (denied or released) — drives nothing.
            return [];
        }

        var deliveries = new List<MixDelivery>();
        var seen = new HashSet<ParticipantId>();
        foreach (NetId net in heldNets)
        {
            foreach (ParticipantId listener in routing.Topology.MembersOf(net))
            {
                if (listener == speaker || !seen.Add(listener))
                {
                    continue;
                }

                MixPlan plan = routing.Planner.PlanFor(listener, holders, _policy);
                if (plan.Sources.Count == 0)
                {
                    continue;
                }

                // Emit this listener's mix only when the frame is from their trigger source, so a
                // multi-source listener is driven once per cycle (by their highest-priority source).
                if (MixSources.Highest(plan.Sources).Speaker == speaker)
                {
                    deliveries.Add(new MixDelivery(listener, plan.Sources));
                }
            }
        }

        return deliveries;
    }
}
