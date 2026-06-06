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

    // Reused per-frame scratch. The media router is driven from a single consumer (the data-plane
    // service), so one tick fully completes — Deliveries is computed AND consumed by the renderer —
    // before the next begins; reusing these across frames is safe and keeps the hot path allocation
    // free. The returned list and each delivery's source buffer alias this state, so the caller must
    // finish with one tick's deliveries before requesting the next.
    private readonly List<MixDelivery> _deliveries = [];
    private readonly List<NetId> _heldNets = [];
    private readonly HashSet<ParticipantId> _seen = [];
    private readonly List<List<MixSource>> _sourcePool = [];

    public MediaRouter(IForceTreeProvider force, IFloorHolders holders, IMixPolicy policy)
    {
        _force = force ?? throw new ArgumentNullException(nameof(force));
        _holders = holders ?? throw new ArgumentNullException(nameof(holders));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <summary>The mixes that <paramref name="speaker"/>'s current frame should drive (empty if it holds no net).</summary>
    public IReadOnlyList<MixDelivery> Deliveries(ParticipantId speaker)
    {
        _deliveries.Clear();

        ForceRouting routing = _force.Current;
        NetMembership membership = routing.Topology.MembershipOf(speaker);
        if (membership.Nets.Count == 0)
        {
            return _deliveries;
        }

        FloorHolders holders = _holders.Current();

        _heldNets.Clear();

        // Index-based loops throughout: a foreach over an IReadOnlyList<T>-typed value boxes the
        // enumerator on the heap every call, which on this per-frame path is exactly the churn we are
        // removing. Count/indexer access allocates nothing.
        IReadOnlyList<NetId> nets = membership.Nets;
        for (int n = 0; n < nets.Count; n++)
        {
            NetId net = nets[n];
            if (holders.TryGetHolder(net, out MixSource holder) && holder.Speaker == speaker)
            {
                _heldNets.Add(net);
            }
        }

        if (_heldNets.Count == 0)
        {
            // The speaker is transmitting without the floor (denied or released) — drives nothing.
            return _deliveries;
        }

        _seen.Clear();
        for (int h = 0; h < _heldNets.Count; h++)
        {
            IReadOnlyList<ParticipantId> members = routing.Topology.MembersOf(_heldNets[h]);
            for (int m = 0; m < members.Count; m++)
            {
                ParticipantId listener = members[m];
                if (listener == speaker || !_seen.Add(listener))
                {
                    continue;
                }

                // Plan into the next pooled buffer; it is only "committed" (kept, and the pool index
                // advanced) when the listener actually receives a delivery, so skipped listeners reuse
                // the same scratch slot.
                List<MixSource> sources = RentSourceBuffer(_deliveries.Count);
                routing.Planner.PlanInto(listener, holders, _policy, sources);
                if (sources.Count == 0)
                {
                    continue;
                }

                // Emit this listener's mix only when the frame is from their trigger source, so a
                // multi-source listener is driven once per cycle (by their highest-priority source).
                if (MixSources.Highest(sources).Speaker == speaker)
                {
                    _deliveries.Add(new MixDelivery(listener, sources));
                }
            }
        }

        return _deliveries;
    }

    private List<MixSource> RentSourceBuffer(int index)
    {
        while (_sourcePool.Count <= index)
        {
            _sourcePool.Add(new List<MixSource>(capacity: 2));
        }

        return _sourcePool[index];
    }
}
