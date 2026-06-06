using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Routing;

namespace Dasim.Radio.MediaService.Tests;

/// <summary>
/// A sample force tree + routing snapshot for the routing tests:
/// <code>
/// CO(100) ─ Alpha(80) ─ A1(60) ─ A1a(40) [p1,p2]
///                              └ A1b(40) [p3]
/// </code>
/// Nets: CO{CO,Alpha} · Alpha{Alpha,A1} · A1{A1,A1a,A1b} · A1a{A1a,p1,p2} · A1b{A1b,p3}.
/// </summary>
internal static class RoutingSample
{
    public const string Co = "CO";
    public const string Alpha = "Alpha";
    public const string A1 = "A1";
    public const string A1a = "A1a";
    public const string A1b = "A1b";
    public const string P1 = "p1";
    public const string P2 = "p2";
    public const string P3 = "p3";

    public static ParticipantId Participant(string id) => new(id);

    public static NetId Net(string id) => new(id);

    public static ForceTree Tree()
    {
        ForceNode p1 = ForceNode.Leaf(P1, "Private 1", ForceNodeKind.Member, new Priority(20));
        ForceNode p2 = ForceNode.Leaf(P2, "Private 2", ForceNodeKind.Member, new Priority(20));
        ForceNode p3 = ForceNode.Leaf(P3, "Private 3", ForceNodeKind.Member, new Priority(20));
        var a1a = new ForceNode(A1a, "Group A1a", ForceNodeKind.Group, new Priority(40), [p1, p2]);
        var a1b = new ForceNode(A1b, "Group A1b", ForceNodeKind.Group, new Priority(40), [p3]);
        var a1 = new ForceNode(A1, "Section A1", ForceNodeKind.Section, new Priority(60), [a1a, a1b]);
        var alpha = new ForceNode(Alpha, "Company Alpha", ForceNodeKind.Company, new Priority(80), [a1]);
        var co = new ForceNode(Co, "Command", ForceNodeKind.Command, new Priority(100), [alpha]);
        return new ForceTree(co);
    }

    public static ForceRouting BuildRouting()
    {
        ForceTree tree = Tree();
        return ForceRouting.Create(1, tree, NetTopology.FromForceTree(tree));
    }
}
