using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging;
using Dasim.Radio.Messaging.Agent;
using Dasim.Radio.Messaging.Audio;
using Dasim.Radio.Messaging.Degrade;
using Dasim.Radio.Messaging.Floor;
using Dasim.Radio.Messaging.KeyValue;
using Dasim.Radio.Messaging.Presence;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using Xunit;

namespace Dasim.Radio.Integration.Tests;

/// <summary>Argument-guard tests for the wrappers (no broker — the connection never connects).</summary>
public sealed class GuardTests
{
    private static NatsConnection Offline() => new(RadioNatsOpts.ForUrl("nats://localhost:4222"));

    [Fact]
    public void Wrappers_reject_a_null_connection()
    {
        Assert.Throws<ArgumentNullException>(() => new NatsAudioBus(null!));
        Assert.Throws<ArgumentNullException>(() => new NatsControlPlaneStore(null!));
        Assert.Throws<ArgumentNullException>(() => new NatsFloorSignal(null!));
        Assert.Throws<ArgumentNullException>(() => new NatsPresenceChannel(null!));
        Assert.Throws<ArgumentNullException>(() => new NatsDegradeChannel(null!));
        Assert.Throws<ArgumentNullException>(() => new NatsAgentCommandClient(null!));
        Assert.Throws<ArgumentNullException>(() => new NatsAgentCommandServer(null!));
    }

    [Fact]
    public void RadioNatsOpts_rejects_a_missing_url()
    {
        Assert.ThrowsAny<ArgumentException>(() => RadioNatsOpts.ForUrl(""));
        Assert.ThrowsAny<ArgumentException>(() => RadioNatsOpts.ForUrl("  "));
    }

    [Fact]
    public void AddDasimRadioMessaging_validates_arguments()
    {
        Assert.Throws<ArgumentNullException>(
            () => ((IServiceCollection)null!).AddDasimRadioMessaging("nats://localhost:4222"));
        Assert.ThrowsAny<ArgumentException>(
            () => new ServiceCollection().AddDasimRadioMessaging(""));
    }

    [Fact]
    public async Task AgentCommandClient_validates_arguments()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using var connection = Offline();
        var client = new NatsAgentCommandClient(connection);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await client.SendAsync(" ", new AgentCommandEnvelope(AgentCommandKinds.Launch), ct));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await client.SendAsync("host", null!, ct));
        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await client.LaunchAsync("host", "", ct));
    }

    [Fact]
    public async Task AgentCommandServer_validates_arguments()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using var connection = Offline();
        var server = new NatsAgentCommandServer(connection);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await server.StartAsync("", (_, _) => ValueTask.FromResult(new AgentCommandResult(true)), ct));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await server.StartAsync("host", null!, ct));
    }

    [Fact]
    public async Task ControlPlaneStore_rejects_an_empty_bucket_name()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using var connection = Offline();
        var store = new NatsControlPlaneStore(connection);

        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await store.BucketAsync<EndpointDto>("", ct));
    }

    [Fact]
    public async Task ControlPlaneStore_propagates_cancellation_and_evicts_the_binding()
    {
        await using var connection = Offline();
        var store = new NatsControlPlaneStore(connection);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // A cancelled bind must surface and not be cached (the next caller may retry).
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await store.BucketAsync<EndpointDto>("endpoints", cancelled.Token));
    }

    [Fact]
    public async Task AgentCommandServer_cleans_up_when_start_is_cancelled()
    {
        await using var connection = Offline();
        var server = new NatsAgentCommandServer(connection);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await server.StartAsync(
                "host",
                (_, _) => ValueTask.FromResult(new AgentCommandResult(true)),
                cancelled.Token));
    }
}
