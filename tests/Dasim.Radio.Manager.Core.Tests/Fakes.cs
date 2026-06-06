using System.Runtime.CompilerServices;
using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.Agent;
using Dasim.Radio.Messaging.Degrade;
using Dasim.Radio.Messaging.KeyValue;
using Dasim.Radio.Messaging.Presence;

namespace Dasim.Radio.Manager.Core.Tests;

/// <summary>An in-memory <see cref="INatsKeyValueStore{T}"/> with create/update revision semantics.</summary>
internal sealed class FakeKeyValueStore<T>(string bucket) : INatsKeyValueStore<T>
{
    private readonly Dictionary<string, (T Value, ulong Revision)> _data = new(StringComparer.Ordinal);
    private ulong _revision;

    public string Bucket => bucket;

    public List<string> Deleted { get; } = [];

    /// <summary>Keys that <see cref="GetKeysAsync"/> yields but that have no value — simulates a key deleted
    /// between listing and reading (the list path must tolerate it).</summary>
    public List<string> GhostKeys { get; } = [];

    public ValueTask<ulong> PutAsync(string key, T value, CancellationToken cancellationToken = default)
    {
        _data[key] = (value, ++_revision);
        return new ValueTask<ulong>(_revision);
    }

    public ValueTask<ulong> CreateAsync(string key, T value, CancellationToken cancellationToken = default)
    {
        if (_data.ContainsKey(key))
        {
            throw new InvalidOperationException($"Key '{key}' already exists.");
        }

        _data[key] = (value, ++_revision);
        return new ValueTask<ulong>(_revision);
    }

    public ValueTask<ulong> UpdateAsync(string key, T value, ulong revision, CancellationToken cancellationToken = default)
    {
        if (!_data.TryGetValue(key, out (T Value, ulong Revision) current) || current.Revision != revision)
        {
            throw new InvalidOperationException("Revision mismatch (optimistic concurrency).");
        }

        _data[key] = (value, ++_revision);
        return new ValueTask<ulong>(_revision);
    }

    public ValueTask<KeyValueEntry<T>?> TryGetAsync(string key, CancellationToken cancellationToken = default) =>
        new(_data.TryGetValue(key, out (T Value, ulong Revision) e) ? new KeyValueEntry<T>(key, e.Value, e.Revision) : null);

    public ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _data.Remove(key);
        Deleted.Add(key);
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<string> GetKeysAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        foreach (string key in _data.Keys.Concat(GhostKeys).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return key;
        }
    }

    public IAsyncEnumerable<KeyValueEntry<T>> WatchAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>An <see cref="IControlPlaneStore"/> that hands out the buckets a test provides; the rest is unused.</summary>
internal sealed class FakeControlPlaneStore(
    INatsKeyValueStore<ClientConfigDto>? configs = null,
    INatsKeyValueStore<ForceTreeDto>? forceTree = null,
    INatsKeyValueStore<PresenceHeartbeat>? presence = null) : IControlPlaneStore
{
    public ValueTask<INatsKeyValueStore<ForceTreeDto>> ForceTreeAsync(CancellationToken cancellationToken = default) =>
        new(forceTree ?? throw new NotSupportedException());

    public ValueTask<INatsKeyValueStore<PresenceHeartbeat>> PresenceAsync(CancellationToken cancellationToken = default) =>
        new(presence ?? throw new NotSupportedException());

    public ValueTask<INatsKeyValueStore<EndpointDto>> EndpointsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<INatsKeyValueStore<FloorStateDto>> FloorStateAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<INatsKeyValueStore<T>> BucketAsync<T>(string bucket, CancellationToken cancellationToken = default) =>
        bucket == Subjects.Buckets.Configs && configs is INatsKeyValueStore<T> typed
            ? new ValueTask<INatsKeyValueStore<T>>(typed)
            : throw new NotSupportedException();
}

/// <summary>An <see cref="IAgentCommandClient"/> that records sent commands and returns a scripted result.</summary>
internal sealed class FakeAgentCommandClient : IAgentCommandClient
{
    public List<(string HostId, AgentCommandEnvelope Command)> Sent { get; } = [];

    public AgentCommandResult Result { get; set; } = new(true);

    public ValueTask<AgentCommandResult> SendAsync(string hostId, AgentCommandEnvelope command, CancellationToken cancellationToken = default)
    {
        Sent.Add((hostId, command));
        return new ValueTask<AgentCommandResult>(Result);
    }

    public ValueTask<AgentCommandResult> LaunchAsync(string hostId, string configId, CancellationToken cancellationToken = default) =>
        SendAsync(hostId, new AgentCommandEnvelope(AgentCommandKinds.Launch, configId), cancellationToken);

    public ValueTask<AgentCommandResult> StopAsync(string hostId, CancellationToken cancellationToken = default) =>
        SendAsync(hostId, new AgentCommandEnvelope(AgentCommandKinds.Stop), cancellationToken);
}

/// <summary>An <see cref="IPresenceChannel"/> that replays scripted heartbeats.</summary>
internal sealed class FakePresenceChannel(params PresenceHeartbeat[] scripted) : IPresenceChannel
{
    public ValueTask PublishAsync(PresenceHeartbeat heartbeat, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public async IAsyncEnumerable<PresenceHeartbeat> SubscribeAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        foreach (PresenceHeartbeat heartbeat in scripted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return heartbeat;
        }
    }
}

/// <summary>An <see cref="IDegradeChannel"/> that records published commands.</summary>
internal sealed class FakeDegradeChannel : IDegradeChannel
{
    public List<DegradeCommand> Published { get; } = [];

    public ValueTask PublishAsync(DegradeCommand command, CancellationToken cancellationToken = default)
    {
        Published.Add(command);
        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerable<DegradeCommand> SubscribeAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
