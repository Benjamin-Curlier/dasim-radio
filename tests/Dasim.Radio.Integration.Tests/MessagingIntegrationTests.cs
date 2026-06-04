using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.Agent;
using Dasim.Radio.Messaging.Audio;
using Dasim.Radio.Messaging.Degrade;
using Dasim.Radio.Messaging.Floor;
using Dasim.Radio.Messaging.KeyValue;
using Dasim.Radio.Messaging.Presence;
using Xunit;

namespace Dasim.Radio.Integration.Tests;

/// <summary>
/// Round-trips each messaging wrapper against a real NATS server: KV (JetStream), Services
/// (request/reply), and core pub/sub for audio, floor and presence.
/// </summary>
public sealed class MessagingIntegrationTests : IClassFixture<NatsContainerFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly NatsContainerFixture _nats;

    public MessagingIntegrationTests(NatsContainerFixture nats) => _nats = nats;

    [Fact]
    public async Task KeyValue_round_trips_a_floor_state_with_optimistic_concurrency()
    {
        _nats.RequireContainer();
        using var cts = new CancellationTokenSource(Timeout);
        await using var connection = _nats.CreateConnection();
        var store = await new NatsControlPlaneStore(connection).FloorStateAsync(cts.Token);

        var idle = new FloorStateDto("net-alpha", HolderParticipantId: null, HolderPriority: null, HeldSinceUtc: null);
        ulong rev1 = await store.PutAsync("net-alpha", idle, cts.Token);

        KeyValueEntry<FloorStateDto>? read = await store.TryGetAsync("net-alpha", cts.Token);
        Assert.NotNull(read);
        Assert.Equal(rev1, read.Value.Revision);
        Assert.Equal(idle, read.Value.Value);

        var held = idle with { HolderParticipantId = "cmd-1", HolderPriority = 100 };
        ulong rev2 = await store.UpdateAsync("net-alpha", held, rev1, cts.Token);
        Assert.NotEqual(rev1, rev2);

        // A stale-revision update is rejected (optimistic concurrency).
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await store.UpdateAsync("net-alpha", idle, rev1, cts.Token));

        read = await store.TryGetAsync("net-alpha", cts.Token);
        Assert.Equal("cmd-1", read!.Value.Value.HolderParticipantId);

        await store.DeleteAsync("net-alpha", cts.Token);
        Assert.Null(await store.TryGetAsync("net-alpha", cts.Token));
    }

    [Fact]
    public async Task AgentCommandService_replies_to_launch_and_stop()
    {
        _nats.RequireContainer();
        using var cts = new CancellationTokenSource(Timeout);
        await using var serverConnection = _nats.CreateConnection();
        await using var clientConnection = _nats.CreateConnection();

        var received = new List<AgentCommandEnvelope>();
        var server = new NatsAgentCommandServer(serverConnection);
        await using IAsyncDisposable handle = await server.StartAsync(
            "host-7",
            (command, _) =>
            {
                received.Add(command);
                return ValueTask.FromResult(new AgentCommandResult(Accepted: true));
            },
            cts.Token);

        var client = new NatsAgentCommandClient(clientConnection);

        AgentCommandResult launch = await client.LaunchAsync("host-7", "config-42", cts.Token);
        Assert.True(launch.Accepted);

        AgentCommandResult stop = await client.StopAsync("host-7", cts.Token);
        Assert.True(stop.Accepted);

        Assert.Collection(
            received,
            c => Assert.Equal(new AgentCommandEnvelope(AgentCommandKinds.Launch, "config-42"), c),
            c => Assert.Equal(new AgentCommandEnvelope(AgentCommandKinds.Stop), c));
    }

    [Fact]
    public async Task KeyValue_lists_and_watches_keys()
    {
        _nats.RequireContainer();
        using var cts = new CancellationTokenSource(Timeout);
        await using var connection = _nats.CreateConnection();
        var store = await new NatsControlPlaneStore(connection).ForceTreeAsync(cts.Token);

        var tree = new ForceTreeDto(Version: 1, new ForceNodeDto("root", "HQ", "Command", 100, []));
        await store.PutAsync("current", tree, cts.Token);

        var keys = new List<string>();
        await foreach (string key in store.GetKeysAsync(cts.Token))
        {
            keys.Add(key);
        }
        Assert.Contains("current", keys);

        // Watch replays the current Put immediately.
        KeyValueEntry<ForceTreeDto>? watched = null;
        await foreach (KeyValueEntry<ForceTreeDto> entry in store.WatchAsync(cts.Token))
        {
            watched = entry;
            break;
        }
        // Field-by-field, not whole-record: ForceNodeDto.Children is an array, so record
        // equality compares it by reference and a round-tripped copy would never match.
        Assert.NotNull(watched);
        Assert.Equal("current", watched.Value.Key);
        Assert.Equal(1, watched.Value.Value.Version);
        Assert.Equal("root", watched.Value.Value.Root.Id);
        Assert.Equal(100, watched.Value.Value.Root.Priority);
    }

    [Fact]
    public async Task AudioBus_round_trips_a_captured_frame_with_its_client_id()
    {
        _nats.RequireContainer();
        using var cts = new CancellationTokenSource(Timeout);
        await using var connection = _nats.CreateConnection();
        var bus = new NatsAudioBus(connection);

        byte[] payload = [0x01, 0x02, 0x03, 0xFF];
        AudioFrame frame = await ReceiveWithRetryAsync(
            bus.SubscribeCapturedAsync(cts.Token),
            ct => bus.PublishCapturedAsync("alpha", payload, ct),
            cts.Token);

        Assert.Equal("alpha", frame.ClientId);
        Assert.Equal(payload, frame.Opus);
    }

    [Fact]
    public async Task AudioBus_round_trips_a_mixed_frame_to_its_listener()
    {
        _nats.RequireContainer();
        using var cts = new CancellationTokenSource(Timeout);
        await using var connection = _nats.CreateConnection();
        var bus = new NatsAudioBus(connection);

        byte[] payload = [0x09, 0x08, 0x07];
        byte[] got = await ReceiveWithRetryAsync(
            bus.SubscribeMixedAsync("alpha", cts.Token),
            ct => bus.PublishMixedAsync("alpha", payload, ct),
            cts.Token);

        Assert.Equal(payload, got);
    }

    [Fact]
    public async Task FloorSignal_round_trips_a_request_and_an_event()
    {
        _nats.RequireContainer();
        using var cts = new CancellationTokenSource(Timeout);
        await using var connection = _nats.CreateConnection();
        var signal = new NatsFloorSignal(connection);

        var request = new FloorRequestMessage("net-bravo", "cmd-1", Priority: 100);
        FloorRequestMessage gotRequest = await ReceiveWithRetryAsync(
            signal.SubscribeRequestsAsync(cts.Token),
            ct => signal.RequestAsync(request, ct),
            cts.Token);
        Assert.Equal(request, gotRequest);

        var release = new FloorReleaseMessage("net-bravo", "cmd-1");
        FloorReleaseMessage gotRelease = await ReceiveWithRetryAsync(
            signal.SubscribeReleasesAsync(cts.Token),
            ct => signal.ReleaseAsync(release, ct),
            cts.Token);
        Assert.Equal(release, gotRelease);

        var @event = new FloorEventMessage("net-bravo", "GrantedWithPreemption", "cmd-1", Preempted: "sgt-2");
        FloorEventMessage gotEvent = await ReceiveWithRetryAsync(
            signal.SubscribeEventsAsync("net-bravo", cts.Token),
            ct => signal.PublishEventAsync(@event, ct),
            cts.Token);
        Assert.Equal(@event, gotEvent);
    }

    [Fact]
    public async Task PresenceChannel_round_trips_a_heartbeat()
    {
        _nats.RequireContainer();
        using var cts = new CancellationTokenSource(Timeout);
        await using var connection = _nats.CreateConnection();
        var presence = new NatsPresenceChannel(connection);

        var heartbeat = new PresenceHeartbeat("host-7", "Alpha", "10.0.0.7", ClientId: "alpha", DateTimeOffset.UnixEpoch);
        PresenceHeartbeat got = await ReceiveWithRetryAsync(
            presence.SubscribeAsync(cts.Token),
            ct => presence.PublishAsync(heartbeat, ct),
            cts.Token);

        Assert.Equal(heartbeat, got);
    }

    [Fact]
    public async Task DegradeChannel_round_trips_a_command()
    {
        _nats.RequireContainer();
        using var cts = new CancellationTokenSource(Timeout);
        await using var connection = _nats.CreateConnection();
        var degrade = new NatsDegradeChannel(connection);

        var command = new DegradeCommand("alpha", NetId: "net-bravo", QualityPercent: 40, ClarityPercent: 75);
        DegradeCommand got = await ReceiveWithRetryAsync(
            degrade.SubscribeAsync(cts.Token),
            ct => degrade.PublishAsync(command, ct),
            cts.Token);

        Assert.Equal(command, got);
    }

    // Core NATS has no replay, so a publish that races ahead of the subscription is lost.
    // Re-publish on a short cadence until the subscriber gets the first message (or we time out).
    private static async Task<T> ReceiveWithRetryAsync<T>(
        IAsyncEnumerable<T> subscription,
        Func<CancellationToken, ValueTask> publish,
        CancellationToken cancellationToken)
    {
        var received = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        var pump = Task.Run(
            async () =>
            {
                await foreach (T item in subscription.WithCancellation(cancellationToken))
                {
                    received.TrySetResult(item);
                    break;
                }
            },
            cancellationToken);

        for (int attempt = 0; attempt < 50 && !received.Task.IsCompleted; attempt++)
        {
            await publish(cancellationToken);
            await Task.WhenAny(received.Task, Task.Delay(100, cancellationToken));
        }

        return await received.Task.WaitAsync(cancellationToken);
    }
}
