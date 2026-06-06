using System.Collections.Concurrent;
using Dasim.Radio.Agent;
using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.Agent;
using Dasim.Radio.Messaging.KeyValue;
using Dasim.Radio.Messaging.Presence;

namespace Dasim.Radio.Agent.Tests;

/// <summary>
/// A one-shot async signal: <see cref="Next"/> captures a task that <see cref="Fire"/> completes, then a
/// fresh one is armed. Lets a test await the next observable side effect deterministically instead of
/// racing a wall-clock poll against a fake-time-provider continuation.
/// </summary>
internal sealed class AsyncSignal
{
    private readonly Lock _gate = new();
    private TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Next
    {
        get
        {
            lock (_gate)
            {
                return _tcs.Task;
            }
        }
    }

    public void Fire()
    {
        TaskCompletionSource toComplete;
        lock (_gate)
        {
            toComplete = _tcs;
            _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        toComplete.TrySetResult();
    }
}

/// <summary>An <see cref="IProcessHandle"/> whose exit state is settable and that records Kill/Dispose.</summary>
internal sealed class FakeProcessHandle(string executablePath, string configId) : IProcessHandle
{
    public string ExecutablePath { get; } = executablePath;

    public string ConfigId { get; } = configId;

    /// <summary>Set to simulate the client exiting on its own (without an explicit stop).</summary>
    public bool HasExited { get; set; }

    public int KillCount { get; private set; }

    public bool Disposed { get; private set; }

    public void Kill()
    {
        KillCount++;
        HasExited = true;
    }

    public void Dispose() => Disposed = true;
}

/// <summary>An <see cref="IProcessRunner"/> that hands out <see cref="FakeProcessHandle"/>s and records starts.</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<FakeProcessHandle> Started { get; } = [];

    /// <summary>When set, the next <see cref="Start"/> throws this instead of returning a handle.</summary>
    public Exception? StartException { get; set; }

    public IProcessHandle Start(string executablePath, string configId)
    {
        if (StartException is not null)
        {
            throw StartException;
        }

        var handle = new FakeProcessHandle(executablePath, configId);
        Started.Add(handle);
        return handle;
    }
}

/// <summary>An <see cref="IPresenceChannel"/> that records published heartbeats; can be told to fail N times first.</summary>
internal sealed class RecordingPresenceChannel : IPresenceChannel
{
    public ConcurrentQueue<PresenceHeartbeat> Published { get; } = new();

    /// <summary>Fired on each successful publish (for deterministic awaiting).</summary>
    public AsyncSignal PublishSignal { get; } = new();

    /// <summary>Number of leading <see cref="PublishAsync"/> calls that should throw before succeeding.</summary>
    public int FailTimes { get; set; }

    private int _failures;

    public ValueTask PublishAsync(PresenceHeartbeat heartbeat, CancellationToken cancellationToken = default)
    {
        if (_failures < FailTimes)
        {
            _failures++;
            throw new InvalidOperationException("presence channel unavailable.");
        }

        Published.Enqueue(heartbeat);
        PublishSignal.Fire();
        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerable<PresenceHeartbeat> SubscribeAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>An <see cref="INatsKeyValueStore{T}"/> for presence that records Puts and Deletes; the rest is unused.</summary>
internal sealed class FakePresenceBucket : INatsKeyValueStore<PresenceHeartbeat>
{
    public List<(string Key, PresenceHeartbeat Value)> Puts { get; } = [];

    public List<string> Deletes { get; } = [];

    /// <summary>Fired on each Put (for deterministic awaiting).</summary>
    public AsyncSignal PutSignal { get; } = new();

    public string Bucket => Subjects.Buckets.Presence;

    public ValueTask<ulong> PutAsync(string key, PresenceHeartbeat value, CancellationToken cancellationToken = default)
    {
        Puts.Add((key, value));
        PutSignal.Fire();
        return ValueTask.FromResult((ulong)Puts.Count);
    }

    public ValueTask DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        Deletes.Add(key);
        return ValueTask.CompletedTask;
    }

    public ValueTask<ulong> CreateAsync(string key, PresenceHeartbeat value, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<ulong> UpdateAsync(string key, PresenceHeartbeat value, ulong revision, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<KeyValueEntry<PresenceHeartbeat>?> TryGetAsync(string key, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<string> GetKeysAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<KeyValueEntry<PresenceHeartbeat>> WatchAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>An <see cref="IControlPlaneStore"/> that hands out a given presence bucket; the rest is unused.</summary>
internal sealed class FakeControlPlaneStore(INatsKeyValueStore<PresenceHeartbeat> presence) : IControlPlaneStore
{
    public ValueTask<INatsKeyValueStore<PresenceHeartbeat>> PresenceAsync(CancellationToken cancellationToken = default) =>
        new(presence);

    public ValueTask<INatsKeyValueStore<ForceTreeDto>> ForceTreeAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<INatsKeyValueStore<EndpointDto>> EndpointsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<INatsKeyValueStore<FloorStateDto>> FloorStateAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<INatsKeyValueStore<T>> BucketAsync<T>(string bucket, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>
/// An <see cref="IAgentCommandServer"/> that captures the registered handler and host id and hands back a
/// handle whose disposal flips <see cref="HandleDisposed"/> — lets a test invoke dispatch directly and
/// assert graceful shutdown.
/// </summary>
internal sealed class FakeAgentCommandServer : IAgentCommandServer
{
    public string? HostId { get; private set; }

    public Func<AgentCommandEnvelope, CancellationToken, ValueTask<AgentCommandResult>>? Handler { get; private set; }

    public bool HandleDisposed { get; private set; }

    public ValueTask<IAsyncDisposable> StartAsync(
        string hostId,
        Func<AgentCommandEnvelope, CancellationToken, ValueTask<AgentCommandResult>> handler,
        CancellationToken cancellationToken = default)
    {
        HostId = hostId;
        Handler = handler;
        return new ValueTask<IAsyncDisposable>(new Handle(this));
    }

    private sealed class Handle(FakeAgentCommandServer owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.HandleDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>An <see cref="IClientController"/> that records calls and returns scripted results.</summary>
internal sealed class FakeClientController : IClientController
{
    public List<string> Launched { get; } = [];

    public List<string> Reconfigured { get; } = [];

    public int StopCount { get; private set; }

    /// <summary>When set, the controller throws this on the next operation (to test the dispatcher guard).</summary>
    public Exception? ThrowOn { get; set; }

    public bool IsRunning { get; set; }

    // Mirror the real controller's invariant: a config id is only advertised while running, so a test
    // can't accidentally assert a "running config" without also marking the controller running.
    public string? CurrentConfigId
    {
        get => IsRunning ? _configId : null;
        set => _configId = value;
    }

    private string? _configId;

    public ValueTask<AgentCommandResult> LaunchAsync(string configId, CancellationToken cancellationToken = default)
    {
        Throw();
        Launched.Add(configId);
        return new ValueTask<AgentCommandResult>(new AgentCommandResult(true));
    }

    public ValueTask<AgentCommandResult> StopAsync(CancellationToken cancellationToken = default)
    {
        Throw();
        StopCount++;
        return new ValueTask<AgentCommandResult>(new AgentCommandResult(true));
    }

    public ValueTask<AgentCommandResult> ReconfigureAsync(string configId, CancellationToken cancellationToken = default)
    {
        Throw();
        Reconfigured.Add(configId);
        return new ValueTask<AgentCommandResult>(new AgentCommandResult(true));
    }

    private void Throw()
    {
        if (ThrowOn is not null)
        {
            throw ThrowOn;
        }
    }
}
