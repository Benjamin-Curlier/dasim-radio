using Dasim.Radio.Core;

namespace Dasim.Radio.Core.Tests;

/// <summary>
/// A small but irregular force tree shared by the routing tests:
/// <code>
/// CO(Command,100)
/// ├─ Alpha(Company,80)
/// │  ├─ A1(Section,60) ── A1a(Group,40) [p1,p2] · A1b(Group,40) [p3]
/// │  └─ A2(Section,60) ── p4                 (a member directly under a section)
/// └─ Bravo(Company,80)                       (a leaf company: no subordinates)
/// </code>
/// </summary>
internal static class SampleForce
{
    public const string Co = "CO";
    public const string Alpha = "Alpha";
    public const string Bravo = "Bravo";
    public const string A1 = "A1";
    public const string A2 = "A2";
    public const string A1a = "A1a";
    public const string A1b = "A1b";
    public const string P1 = "p1";
    public const string P2 = "p2";
    public const string P3 = "p3";
    public const string P4 = "p4";

    public static ParticipantId Participant(string id) => new(id);

    public static NetId Net(string id) => new(id);

    public static ForceTree BuildTree()
    {
        ForceNode p1 = ForceNode.Leaf(P1, "Private 1", ForceNodeKind.Member, new Priority(20));
        ForceNode p2 = ForceNode.Leaf(P2, "Private 2", ForceNodeKind.Member, new Priority(20));
        ForceNode p3 = ForceNode.Leaf(P3, "Private 3", ForceNodeKind.Member, new Priority(20));
        ForceNode p4 = ForceNode.Leaf(P4, "Private 4", ForceNodeKind.Member, new Priority(20));

        var a1a = new ForceNode(A1a, "Group A1a", ForceNodeKind.Group, new Priority(40), [p1, p2]);
        var a1b = new ForceNode(A1b, "Group A1b", ForceNodeKind.Group, new Priority(40), [p3]);
        var a1 = new ForceNode(A1, "Section A1", ForceNodeKind.Section, new Priority(60), [a1a, a1b]);
        var a2 = new ForceNode(A2, "Section A2", ForceNodeKind.Section, new Priority(60), [p4]);
        var alpha = new ForceNode(Alpha, "Company Alpha", ForceNodeKind.Company, new Priority(80), [a1, a2]);
        ForceNode bravo = ForceNode.Leaf(Bravo, "Company Bravo", ForceNodeKind.Company, new Priority(80));
        var co = new ForceNode(Co, "Command", ForceNodeKind.Command, new Priority(100), [alpha, bravo]);

        return new ForceTree(co);
    }

    public static NetTopology BuildTopology() => NetTopology.FromForceTree(BuildTree());
}
