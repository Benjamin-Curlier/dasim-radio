using Dasim.Radio.Contracts;
using Dasim.Radio.Manager.Core;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dasim.Radio.Manager.Core.Tests;

public sealed class PostDirectoryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PresenceHeartbeat Beat(string host, DateTimeOffset at) =>
        new(host, host, "10.0.0.1", null, at);

    private static PostDirectoryService Build(
        FakeKeyValueStore<PresenceHeartbeat>? presence = null,
        FakePresenceChannel? channel = null,
        TimeSpan? staleAfter = null)
    {
        var options = Options.Create(new ManagerOptions { PresenceStaleAfter = staleAfter ?? TimeSpan.FromSeconds(15) });
        return new PostDirectoryService(
            new FakeControlPlaneStore(presence: presence ?? new FakeKeyValueStore<PresenceHeartbeat>(Subjects.Buckets.Presence)),
            channel ?? new FakePresenceChannel(),
            options,
            new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task Snapshot_reads_the_presence_bucket()
    {
        var bucket = new FakeKeyValueStore<PresenceHeartbeat>(Subjects.Buckets.Presence);
        await bucket.PutAsync("post-01", Beat("post-01", Now), Ct);
        await bucket.PutAsync("post-02", Beat("post-02", Now), Ct);
        PostDirectoryService service = Build(presence: bucket);

        IReadOnlyList<PresenceHeartbeat> snapshot = await service.SnapshotAsync(Ct);

        Assert.Equal(2, snapshot.Count);
    }

    [Fact]
    public async Task Online_posts_flags_a_stale_heartbeat()
    {
        var bucket = new FakeKeyValueStore<PresenceHeartbeat>(Subjects.Buckets.Presence);
        await bucket.PutAsync("fresh", Beat("fresh", Now - TimeSpan.FromSeconds(5)), Ct);
        await bucket.PutAsync("stale", Beat("stale", Now - TimeSpan.FromSeconds(30)), Ct);
        PostDirectoryService service = Build(presence: bucket, staleAfter: TimeSpan.FromSeconds(15));

        IReadOnlyList<PostStatus> posts = await service.OnlinePostsAsync(Ct);

        Assert.False(posts.Single(p => p.Heartbeat.HostId == "fresh").IsStale);
        Assert.True(posts.Single(p => p.Heartbeat.HostId == "stale").IsStale);
    }

    [Fact]
    public async Task Watch_surfaces_channel_heartbeats()
    {
        var channel = new FakePresenceChannel(Beat("post-01", Now), Beat("post-02", Now));
        PostDirectoryService service = Build(channel: channel);

        var seen = new List<string>();
        await foreach (PresenceHeartbeat heartbeat in service.WatchAsync(Ct))
        {
            seen.Add(heartbeat.HostId);
        }

        Assert.Equal(["post-01", "post-02"], seen);
    }
}
