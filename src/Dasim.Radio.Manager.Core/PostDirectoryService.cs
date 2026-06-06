using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.KeyValue;
using Dasim.Radio.Messaging.Presence;
using Microsoft.Extensions.Options;

namespace Dasim.Radio.Manager.Core;

/// <summary>A discovered post with whether its last heartbeat is now stale.</summary>
public sealed record PostStatus(PresenceHeartbeat Heartbeat, bool IsStale);

/// <summary>Discovers live posts via the durable presence KV snapshot and the low-latency presence channel.</summary>
public interface IPostDirectoryService
{
    /// <summary>The current durable presence snapshot (from the TTL'd <c>presence</c> bucket).</summary>
    ValueTask<IReadOnlyList<PresenceHeartbeat>> SnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>The snapshot annotated with staleness against <c>PresenceStaleAfter</c>.</summary>
    ValueTask<IReadOnlyList<PostStatus>> OnlinePostsAsync(CancellationToken cancellationToken = default);

    /// <summary>Live heartbeat stream for low-latency discovery.</summary>
    IAsyncEnumerable<PresenceHeartbeat> WatchAsync(CancellationToken cancellationToken = default);
}

/// <summary>Implementation of <see cref="IPostDirectoryService"/>.</summary>
public sealed class PostDirectoryService(
    IControlPlaneStore store,
    IPresenceChannel channel,
    IOptions<ManagerOptions> options,
    TimeProvider timeProvider) : IPostDirectoryService
{
    private readonly IControlPlaneStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IPresenceChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    private readonly ManagerOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async ValueTask<IReadOnlyList<PresenceHeartbeat>> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        INatsKeyValueStore<PresenceHeartbeat> bucket = await _store.PresenceAsync(cancellationToken).ConfigureAwait(false);

        var posts = new List<PresenceHeartbeat>();
        await foreach (string key in bucket.GetKeysAsync(cancellationToken).ConfigureAwait(false))
        {
            KeyValueEntry<PresenceHeartbeat>? entry = await bucket.TryGetAsync(key, cancellationToken).ConfigureAwait(false);
            if (entry is { } found)
            {
                posts.Add(found.Value);
            }
        }

        return posts;
    }

    public async ValueTask<IReadOnlyList<PostStatus>> OnlinePostsAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        IReadOnlyList<PresenceHeartbeat> snapshot = await SnapshotAsync(cancellationToken).ConfigureAwait(false);
        return [.. snapshot.Select(hb => new PostStatus(hb, now - hb.TimestampUtc > _options.PresenceStaleAfter))];
    }

    public IAsyncEnumerable<PresenceHeartbeat> WatchAsync(CancellationToken cancellationToken = default) =>
        _channel.SubscribeAsync(cancellationToken);
}
