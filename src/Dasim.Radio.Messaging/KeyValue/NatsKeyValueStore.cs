using System.Runtime.CompilerServices;
using NATS.Client.KeyValueStore;

namespace Dasim.Radio.Messaging.KeyValue;

/// <summary>Adapts a NATS <see cref="INatsKVStore"/> to the typed <see cref="INatsKeyValueStore{T}"/>.</summary>
internal sealed class NatsKeyValueStore<T> : INatsKeyValueStore<T>
{
    private readonly INatsKVStore _store;

    public NatsKeyValueStore(INatsKVStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public string Bucket => _store.Bucket;

    public ValueTask<ulong> PutAsync(string key, T value, CancellationToken cancellationToken = default) =>
        _store.PutAsync(key, value, cancellationToken: cancellationToken);

    public ValueTask<ulong> CreateAsync(string key, T value, CancellationToken cancellationToken = default) =>
        _store.CreateAsync(key, value, cancellationToken: cancellationToken);

    public ValueTask<ulong> UpdateAsync(string key, T value, ulong revision, CancellationToken cancellationToken = default) =>
        _store.UpdateAsync(key, value, revision, cancellationToken: cancellationToken);

    public async ValueTask<KeyValueEntry<T>?> TryGetAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            NatsKVEntry<T> entry = await _store.GetEntryAsync<T>(key, cancellationToken: cancellationToken);
            return entry.Value is null ? null : new KeyValueEntry<T>(entry.Key, entry.Value, entry.Revision);
        }
        catch (NatsKVKeyNotFoundException)
        {
            return null;
        }
        catch (NatsKVKeyDeletedException)
        {
            return null;
        }
    }

    public ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        _store.DeleteAsync(key, cancellationToken: cancellationToken);

    public async IAsyncEnumerable<string> GetKeysAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (string key in _store.GetKeysAsync(cancellationToken: cancellationToken))
        {
            yield return key;
        }
    }

    public async IAsyncEnumerable<KeyValueEntry<T>> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (NatsKVEntry<T> entry in _store.WatchAsync<T>(cancellationToken: cancellationToken))
        {
            if (entry.Operation == NatsKVOperation.Put && entry.Value is not null)
            {
                yield return new KeyValueEntry<T>(entry.Key, entry.Value, entry.Revision);
            }
        }
    }
}
