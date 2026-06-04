namespace Dasim.Radio.Messaging.KeyValue;

/// <summary>A value read from a KV bucket together with its revision.</summary>
public readonly record struct KeyValueEntry<T>(string Key, T Value, ulong Revision);

/// <summary>
/// A typed view over a single JetStream KV bucket. Wraps the NATS KV store with the
/// Dasim.Radio value type <typeparamref name="T"/> and a small, intention-revealing surface.
/// </summary>
public interface INatsKeyValueStore<T>
{
    /// <summary>The bucket name.</summary>
    string Bucket { get; }

    /// <summary>Upserts <paramref name="value"/> and returns the new revision.</summary>
    ValueTask<ulong> PutAsync(string key, T value, CancellationToken cancellationToken = default);

    /// <summary>Writes <paramref name="value"/> only if the key does not yet exist.</summary>
    ValueTask<ulong> CreateAsync(string key, T value, CancellationToken cancellationToken = default);

    /// <summary>Updates the key only if its current revision is <paramref name="revision"/> (optimistic concurrency).</summary>
    ValueTask<ulong> UpdateAsync(string key, T value, ulong revision, CancellationToken cancellationToken = default);

    /// <summary>Reads the current value, or <c>null</c> if the key is absent or deleted.</summary>
    ValueTask<KeyValueEntry<T>?> TryGetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Deletes the key (history is retained).</summary>
    ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Enumerates the current keys in the bucket.</summary>
    IAsyncEnumerable<string> GetKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>Watches live <c>Put</c> updates (starting with the current values).</summary>
    IAsyncEnumerable<KeyValueEntry<T>> WatchAsync(CancellationToken cancellationToken = default);
}
