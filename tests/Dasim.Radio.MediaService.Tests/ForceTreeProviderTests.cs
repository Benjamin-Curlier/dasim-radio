using System.Diagnostics;
using Dasim.Radio.Contracts;
using Dasim.Radio.MediaService.Routing;
using Dasim.Radio.Messaging.KeyValue;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dasim.Radio.MediaService.Tests;

public sealed class ForceTreeProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static KeyValueEntry<ForceTreeDto> Entry(string key, ForceTreeDto tree, ulong revision) =>
        new(key, tree, revision);

    // A valid single-net tree: a root with one member child, so FromForceTree yields exactly one net.
    private static ForceTreeDto Tree(int version, string rootId, string kind = "Command") =>
        new(version, new ForceNodeDto(rootId, rootId, kind, 100,
            [new ForceNodeDto($"{rootId}-m", "Member", "Member", 20, [])]));

    private static ForceTreeProvider Provider(params KeyValueEntry<ForceTreeDto>[] entries) =>
        new(new ForceTreeControlPlaneStore(new ScriptedForceTreeBucket(entries)),
            NullLogger<ForceTreeProvider>.Instance);

    [Fact]
    public void Current_is_empty_before_any_tree_loads()
    {
        ForceTreeProvider provider = Provider();

        Assert.Equal(0, provider.Current.Version);
        Assert.Null(provider.Current.Tree);
        Assert.Empty(provider.Current.Topology.Nets);
    }

    [Fact]
    public async Task Applies_the_watched_force_tree()
    {
        ForceTreeProvider provider = Provider(Entry("current", Tree(1, "CO"), 1));

        await provider.StartAsync(Ct);
        try
        {
            await WaitForAsync(() => provider.Current.Version == 1);
        }
        finally
        {
            await provider.StopAsync(Ct);
        }

        Assert.NotNull(provider.Current.Tree);
        Assert.Single(provider.Current.Topology.Nets);
    }

    [Fact]
    public async Task Ignores_keys_other_than_current()
    {
        ForceTreeProvider provider = Provider(
            Entry("stale", Tree(5, "OLD"), 1),
            Entry("current", Tree(1, "CO"), 2));

        await provider.StartAsync(Ct);
        try
        {
            await WaitForAsync(() => provider.Current.Version == 1);
        }
        finally
        {
            await provider.StopAsync(Ct);
        }

        Assert.Equal("CO", provider.Current.Tree!.Root.Id);
    }

    [Fact]
    public async Task Keeps_the_previous_tree_when_a_new_one_is_invalid()
    {
        ForceTreeProvider provider = Provider(
            Entry("current", Tree(1, "CO"), 1),
            Entry("current", Tree(2, "BAD", kind: "Battalion"), 2));

        await provider.StartAsync(Ct);
        try
        {
            await WaitForAsync(() => provider.Current.Version == 1);
            await Task.Delay(100, Ct); // let the invalid v2 be processed and rejected
        }
        finally
        {
            await provider.StopAsync(Ct);
        }

        Assert.Equal(1, provider.Current.Version);
        Assert.Equal("CO", provider.Current.Tree!.Root.Id);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(20, Ct);
        }

        Assert.True(condition(), "Condition was not met within the timeout.");
    }
}
