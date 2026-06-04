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
    // Points at a closed port and never retries, so any operation that needs the broker fails
    // fast and deterministically (used by the error-path tests below).
    private static NatsConnection Offline() =>
        new(RadioNatsOpts.ForUrl("nats://127.0.0.1:14222") with
        {
            RetryOnInitialConnect = false,
            ConnectTimeout = TimeSpan.FromSeconds(2),
        });

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
    public async Task ControlPlaneStore_propagates_a_failed_binding_and_does_not_cache_it()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using var connection = Offline();
        var store = new NatsControlPlaneStore(connection);

        // The bind can't reach the broker, so it faults; the failure surfaces and is evicted, so a
        // second call re-binds (and fails) rather than returning a poisoned cached task.
        await Assert.ThrowsAnyAsync<Exception>(async () => await store.BucketAsync<EndpointDto>("endpoints", ct));
        await Assert.ThrowsAnyAsync<Exception>(async () => await store.BucketAsync<EndpointDto>("endpoints", ct));
    }

    [Fact]
    public async Task AgentCommandServer_cleans_up_when_start_fails()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using var connection = Offline();
        var server = new NatsAgentCommandServer(connection);

        await Assert.ThrowsAnyAsync<Exception>(async () => await server.StartAsync(
            "host",
            (_, _) => ValueTask.FromResult(new AgentCommandResult(true)),
            ct));
    }
}
