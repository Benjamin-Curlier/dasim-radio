using Dasim.Radio.Contracts;
using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Routing;
using Xunit;

namespace Dasim.Radio.MediaService.Tests;

public sealed class ForceTreeMapperTests
{
    [Fact]
    public void Maps_a_dto_tree_onto_the_domain_tree()
    {
        var dto = new ForceTreeDto(1, new ForceNodeDto("CO", "Command", "Command", 100,
            [new ForceNodeDto("p1", "Private", "Member", 20, [])]));

        ForceTree tree = ForceTreeMapper.ToDomain(dto);

        Assert.Equal("CO", tree.Root.Id);
        Assert.Equal(ForceNodeKind.Command, tree.Root.Kind);
        Assert.Equal(new Priority(100), tree.Root.Priority);
        ForceNode child = Assert.Single(tree.Root.Children);
        Assert.Equal(ForceNodeKind.Member, child.Kind);
        Assert.Equal(new Priority(20), child.Priority);
    }

    [Fact]
    public void Parses_echelon_names_case_insensitively()
    {
        var dto = new ForceTreeDto(1, new ForceNodeDto("s1", "Section", "section", 60, []));

        Assert.Equal(ForceNodeKind.Section, ForceTreeMapper.ToDomain(dto).Root.Kind);
    }

    [Fact]
    public void Rejects_an_unknown_echelon()
    {
        var dto = new ForceTreeDto(1, new ForceNodeDto("x", "Mystery", "Battalion", 50, []));

        Assert.Throws<ArgumentException>(() => ForceTreeMapper.ToDomain(dto));
    }
}
