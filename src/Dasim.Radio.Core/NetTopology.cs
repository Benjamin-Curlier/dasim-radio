namespace Dasim.Radio.Core;

/// <summary>
/// The nets a participant belongs to, derived from the force tree. In the subtree-net model a
/// non-leaf node owns one net (<see cref="OwnedNet"/>) holding itself plus its direct children, and
/// every node is also a member of its parent's net (<see cref="ParentNet"/>). So a leader sits on
/// two nets — the one it owns (talk <em>down</em>) and its parent's (talk <em>up</em>) — while a
/// leaf member and the root each sit on one.
/// </summary>
public sealed record NetMembership(
    ParticipantId Participant,
    NetId? OwnedNet,
    NetId? ParentNet,
    IReadOnlyList<NetId> Nets)
{
    /// <summary>An empty membership (the participant is on no net), returned for unknown participants.</summary>
    public static NetMembership None(ParticipantId participant) => new(participant, null, null, []);

    /// <summary>
    /// The net a single push-to-talk targets by default: the net the participant owns (talk down),
    /// falling back to the parent net for a leaf member who owns none. Null only for an isolated root.
    /// </summary>
    public NetId? DefaultTransmitNet => OwnedNet ?? ParentNet;
}

/// <summary>
/// Subtree-net topology derived from a <see cref="ForceTree"/>: one net per non-leaf node, whose
/// members are that node plus its direct children. Maps each participant to the (≤2) nets it belongs
/// to. Immutable; rebuild it when a new <c>force_tree</c> version is imported. The net id equals the
/// owning node's id, so it lines up 1:1 with the floor's <see cref="NetId"/>.
/// </summary>
public sealed class NetTopology
{
    private readonly IReadOnlyDictionary<NetId, IReadOnlyList<ParticipantId>> _members;
    private readonly IReadOnlyDictionary<ParticipantId, NetMembership> _membership;

    private NetTopology(
        IReadOnlyDictionary<NetId, IReadOnlyList<ParticipantId>> members,
        IReadOnlyDictionary<ParticipantId, NetMembership> membership)
    {
        _members = members;
        _membership = membership;
    }

    /// <summary>An empty topology (no nets) — a host's state before any force tree has been imported.</summary>
    public static NetTopology Empty { get; } = new(
        new Dictionary<NetId, IReadOnlyList<ParticipantId>>(),
        new Dictionary<ParticipantId, NetMembership>());

    /// <summary>Every net in the topology (one per non-leaf node).</summary>
    public IReadOnlyCollection<NetId> Nets => (IReadOnlyCollection<NetId>)_members.Keys;

    /// <summary>Builds the topology from a force tree.</summary>
    public static NetTopology FromForceTree(ForceTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var members = new Dictionary<NetId, IReadOnlyList<ParticipantId>>();
        var owned = new Dictionary<ParticipantId, NetId?>();
        var parent = new Dictionary<ParticipantId, NetId?>();

        foreach (ForceNode node in tree.Enumerate())
        {
            var participant = new ParticipantId(node.Id);
            // Every node is a participant, even a leaf or an isolated root that owns no net.
            owned.TryAdd(participant, null);
            parent.TryAdd(participant, null);

            if (node.Children.Count == 0)
            {
                continue;
            }

            var net = new NetId(node.Id);
            owned[participant] = net;

            var roster = new List<ParticipantId>(node.Children.Count + 1) { participant };
            foreach (ForceNode child in node.Children)
            {
                var childParticipant = new ParticipantId(child.Id);
                roster.Add(childParticipant);
                owned.TryAdd(childParticipant, null);
                parent[childParticipant] = net;
            }

            members[net] = roster;
        }

        var membership = new Dictionary<ParticipantId, NetMembership>(owned.Count);
        foreach ((ParticipantId participant, NetId? ownedNet) in owned)
        {
            NetId? parentNet = parent[participant];
            NetId[] nets = (ownedNet, parentNet) switch
            {
                ({ } o, { } p) => [o, p],
                ({ } o, null) => [o],
                (null, { } p) => [p],
                (null, null) => [],
            };
            membership[participant] = new NetMembership(participant, ownedNet, parentNet, nets);
        }

        return new NetTopology(members, membership);
    }

    /// <summary>The participants on a net (its owner plus that owner's direct children); empty if the net is unknown.</summary>
    public IReadOnlyList<ParticipantId> MembersOf(NetId net) =>
        _members.GetValueOrDefault(net, []);

    /// <summary>The nets a participant belongs to; an empty membership if the participant is not in the tree.</summary>
    public NetMembership MembershipOf(ParticipantId participant) =>
        _membership.GetValueOrDefault(participant) ?? NetMembership.None(participant);
}
