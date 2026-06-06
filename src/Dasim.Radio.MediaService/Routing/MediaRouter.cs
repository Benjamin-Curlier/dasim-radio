using Dasim.Radio.Core;

namespace Dasim.Radio.MediaService.Routing;

/// <summary>
/// Decides where a captured frame goes. Given the speaker of a frame, it returns the listeners that
/// should receive it: the members of every net the speaker currently holds whose mix (per the
/// <see cref="IMixPolicy"/>) actually resolves to that speaker. Under the priority-override policy a
/// listener resolves to a single source, so forwarding the speaker's frame to each recipient is a
/// zero-transcode pass-through — the common case. (Summing two sources for the additive policy and
/// per-listener degradation are later slices; this router forwards bytes only.)
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

    /// <summary>
    /// The listeners that should receive <paramref name="speaker"/>'s current frame. Empty when the
    /// speaker holds no net (no floor) or every member is hearing a higher net instead.
    /// </summary>
    public IReadOnlyList<ParticipantId> Recipients(ParticipantId speaker)
    {
        ForceRouting routing = _force.Current;
        NetMembership membership = routing.Topology.MembershipOf(speaker);
        if (membership.Nets.Count == 0)
        {
            return [];
        }

        FloorHolders holders = _holders.Current();

        // The net(s) this speaker actually holds — net-select PTT means usually one.
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
            // The speaker is transmitting without the floor (denied or released) — drop it.
            return [];
        }

        var recipients = new List<ParticipantId>();
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
                if (Hears(plan, speaker))
                {
                    recipients.Add(listener);
                }
            }
        }

        return recipients;
    }

    private static bool Hears(MixPlan plan, ParticipantId speaker)
    {
        foreach (MixSource source in plan.Sources)
        {
            if (source.Speaker == speaker)
            {
                return true;
            }
        }

        return false;
    }
}
