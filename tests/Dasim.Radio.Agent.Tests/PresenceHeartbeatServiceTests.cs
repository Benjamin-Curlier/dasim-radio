using Dasim.Radio.Agent;
using Dasim.Radio.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dasim.Radio.Agent.Tests;

public sealed class PresenceHeartbeatServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        PresenceHeartbeatService Service,
        RecordingPresenceChannel Channel,
        FakePresenceBucket Bucket,
        FakeClientController Controller,
        FakeTimeProvider Time);

    private static Harness Build(Action<FakeClientController>? configureController = null, int channelFailTimes = 0)
    {
        var channel = new RecordingPresenceChannel { FailTimes = channelFailTimes };
        var bucket = new FakePresenceBucket();
        var store = new FakeControlPlaneStore(bucket);
        var controller = new FakeClientController();
        configureController?.Invoke(controller);
        var time = new FakeTimeProvider(Now);
        var options = Options.Create(new AgentOptions
        {
            HostId = "post-01",
            HostName = "Post 01",
            IpAddress = "10.0.0.1",
            HeartbeatInterval = Interval,
        });

        var service = new PresenceHeartbeatService(
            channel, store, controller, options, time, NullLogger<PresenceHeartbeatService>.Instance);

        return new Harness(service, channel, bucket, controller, time);
    }

    [Fact]
    public async Task Beats_immediately_on_start_to_both_channel_and_kv()
    {
        Harness h = Build();
        Task firstBeat = h.Bucket.PutSignal.Next; // arm before start — the first beat needs no time advance

        await h.Service.StartAsync(Ct);
        try
        {
            await firstBeat.WaitAsync(Ct);

            Assert.Single(h.Channel.Published);
            (string key, PresenceHeartbeat value) = Assert.Single(h.Bucket.Puts);
            Assert.Equal("post-01", key);
            Assert.Equal(Now, value.TimestampUtc); // TimeProvider, never DateTime.Now
        }
        finally
        {
            await h.Service.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task Beats_again_after_each_interval()
    {
        Harness h = Build();
        Task firstBeat = h.Bucket.PutSignal.Next;

        await h.Service.StartAsync(Ct);
        try
        {
            await firstBeat.WaitAsync(Ct); // immediate beat (Puts == 1)

            Task secondBeat = h.Bucket.PutSignal.Next;
            await AdvanceUntilAsync(h.Time, secondBeat);

            Assert.True(h.Bucket.Puts.Count >= 2);
            Assert.True(h.Channel.Published.Count >= 2);
            Assert.All(h.Bucket.Puts, put => Assert.Equal("post-01", put.Key));
        }
        finally
        {
            await h.Service.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task Heartbeat_carries_no_client_id_when_idle()
    {
        Harness h = Build(c => c.CurrentConfigId = null);
        Task firstBeat = h.Channel.PublishSignal.Next;

        await h.Service.StartAsync(Ct);
        try
        {
            await firstBeat.WaitAsync(Ct);
            Assert.True(h.Channel.Published.TryPeek(out PresenceHeartbeat? beat));
            Assert.Null(beat!.ClientId);
        }
        finally
        {
            await h.Service.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task Heartbeat_carries_the_running_config_id()
    {
        Harness h = Build(c =>
        {
            c.IsRunning = true;
            c.CurrentConfigId = "cfg-7";
        });
        Task firstBeat = h.Channel.PublishSignal.Next;

        await h.Service.StartAsync(Ct);
        try
        {
            await firstBeat.WaitAsync(Ct);
            Assert.True(h.Channel.Published.TryPeek(out PresenceHeartbeat? beat));
            Assert.Equal("cfg-7", beat!.ClientId);
        }
        finally
        {
            await h.Service.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task Survives_a_transient_channel_fault_and_keeps_beating()
    {
        Harness h = Build(channelFailTimes: 1); // the first beat's channel publish throws
        Task firstBeat = h.Bucket.PutSignal.Next;

        await h.Service.StartAsync(Ct);
        try
        {
            await firstBeat.WaitAsync(Ct);

            // The first beat's channel publish faulted (nothing published yet), but the KV write still
            // happened — proof the fault was swallowed rather than killing the beat.
            Assert.Empty(h.Channel.Published);
            Assert.Single(h.Bucket.Puts);

            // The loop survived: a later tick reaches the channel (now recovered) and writes KV again.
            Task recovered = h.Channel.PublishSignal.Next;
            await AdvanceUntilAsync(h.Time, recovered);

            Assert.True(h.Channel.Published.Count >= 1); // the post-recovery beat
            Assert.True(h.Bucket.Puts.Count >= 2); // strictly more than the single pre-recovery write
        }
        finally
        {
            await h.Service.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task Deregisters_presence_on_graceful_stop()
    {
        Harness h = Build();
        Task firstBeat = h.Bucket.PutSignal.Next;

        await h.Service.StartAsync(Ct);
        await firstBeat.WaitAsync(Ct); // ensure the bucket was bound, so a deregister is attempted
        await h.Service.StopAsync(Ct);

        Assert.Contains("post-01", h.Bucket.Deletes);
    }

    // Drives the PeriodicTimer deterministically: advance one interval, yield so the service's tick
    // continuation (and, on the first call, its timer registration) can run, and repeat until the
    // awaited side effect fires. No fixed wall-clock budget, so it can't flake under a loaded runner;
    // the test's own cancellation token bounds it if something is genuinely wrong.
    private static async Task AdvanceUntilAsync(FakeTimeProvider time, Task signal)
    {
        while (!signal.IsCompleted)
        {
            Ct.ThrowIfCancellationRequested();
            time.Advance(Interval);
            await Task.Yield();
        }

        await signal;
    }
}
