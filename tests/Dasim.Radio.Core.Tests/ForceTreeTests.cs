using Dasim.Radio.Core;
using Xunit;

namespace Dasim.Radio.Core.Tests;

public sealed class ForceTreeTests
{
    private static ForceTree BuildSample()
    {
        ForceNode soldier = ForceNode.Leaf("m1", "Soldier", ForceNodeKind.Member, new Priority(1));
        var group = new ForceNode("g1", "Group 1", ForceNodeKind.Group, new Priority(2), [soldier]);
        var section = new ForceNode("s1", "Section 1", ForceNodeKind.Section, new Priority(3), [group]);
        return new ForceTree(section);
    }

    [Fact]
    public void Find_returns_node_by_id()
    {
        Assert.Equal("Group 1", BuildSample().Find("g1")!.Name);
    }

    [Fact]
    public void Find_returns_null_for_unknown_id()
    {
        Assert.Null(BuildSample().Find("nope"));
    }

    [Fact]
    public void Enumerate_visits_every_node()
    {
        string[] ids = BuildSample()
            .Enumerate()
            .Select(n => n.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["g1", "m1", "s1"], ids);
    }

    [Fact]
    public void Duplicate_ids_are_rejected()
    {
        var duplicate = new ForceNode("x", "B", ForceNodeKind.Member, new Priority(1), []);
        var root = new ForceNode("x", "A", ForceNodeKind.Section, new Priority(2), [duplicate]);

        Assert.Throws<ArgumentException>(() => new ForceTree(root));
    }
}
