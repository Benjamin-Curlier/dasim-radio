using Dasim.Radio.Messaging;
using NATS.Client.Core;
using Testcontainers.Nats;
using Xunit;

namespace Dasim.Radio.Integration.Tests;

/// <summary>
/// Spins up a throwaway NATS server (JetStream enabled) for the test class. When Docker is not
/// reachable — e.g. a Windows CI runner without Linux containers — startup fails softly and
/// <see cref="Available"/> stays false so tests skip instead of erroring.
/// </summary>
public sealed class NatsContainerFixture : IAsyncLifetime
{
    private const string NatsImage = "nats:2.10";

    private readonly NatsContainer _container = new NatsBuilder(NatsImage).Build();

    /// <summary>True when the container started and tests can run.</summary>
    public bool Available { get; private set; }

    /// <summary>Why the container is unavailable (for the skip message), or null.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>NATS client URL for the running container.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    // Generous enough for a cold image pull on CI, bounded so a host without (Linux) Docker —
    // e.g. a Windows runner — fails fast to the skip path instead of hanging the run.
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);

    public async ValueTask InitializeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(StartupTimeout);
            await _container.StartAsync(cts.Token);
            ConnectionString = _container.GetConnectionString();
            Available = true;
        }
        catch (Exception ex)
        {
            Available = false;
            SkipReason = $"NATS test container unavailable (is Docker running with Linux containers?): {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Skips the calling test when the container could not start.</summary>
    public void RequireContainer() => Assert.SkipUnless(Available, SkipReason ?? "NATS container unavailable.");

    /// <summary>A fresh connection wired with the Dasim.Radio serializer registry.</summary>
    public NatsConnection CreateConnection() => new(RadioNatsOpts.ForUrl(ConnectionString));
}
