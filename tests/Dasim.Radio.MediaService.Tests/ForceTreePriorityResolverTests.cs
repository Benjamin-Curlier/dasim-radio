using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Floor;
using Dasim.Radio.MediaService.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dasim.Radio.MediaService.Tests;

public sealed class ForceTreePriorityResolverTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Resolves_the_force_tree_priority_ignoring_the_wire_value()
    {
        var sut = new ForceTreePriorityResolver(
            new FakeForceTreeProvider(RoutingSample.BuildRouting()), NullLogger<ForceTreePriorityResolver>.Instance);

        // A1 is a section: authored priority 60, not the inflated 9999 the "client" claimed.
        Priority resolved = await sut.ResolveAsync(new ParticipantId("A1"), new Priority(9999), Ct);

        Assert.Equal(new Priority(60), resolved);
    }

    [Fact]
    public async Task An_unknown_participant_resolves_to_the_lowest_priority()
    {
        var sut = new ForceTreePriorityResolver(
            new FakeForceTreeProvider(RoutingSample.BuildRouting()), NullLogger<ForceTreePriorityResolver>.Instance);

        Priority resolved = await sut.ResolveAsync(new ParticipantId("ghost"), new Priority(9999), Ct);

        Assert.Equal(new Priority(int.MinValue), resolved);
    }

    [Fact]
    public async Task With_no_tree_loaded_everyone_resolves_to_the_lowest_priority()
    {
        var sut = new ForceTreePriorityResolver(
            new FakeForceTreeProvider(ForceRouting.Empty), NullLogger<ForceTreePriorityResolver>.Instance);

        Priority resolved = await sut.ResolveAsync(new ParticipantId("A1"), new Priority(5), Ct);

        Assert.Equal(new Priority(int.MinValue), resolved);
    }
}
