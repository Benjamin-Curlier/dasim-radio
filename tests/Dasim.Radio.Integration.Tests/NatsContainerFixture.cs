using Dasim.Radio.Messaging;
using NATS.Client.Core;
using Testcontainers.Nats;
using Xunit;

namespace Dasim.Radio.Integration.Tests;

/// <summary>
/// Spins up a throwaway NATS server (JetStream enabled) for the test class. When Docker is not
/// reachable — e.g. a Windows CI runner with no Docker daemon — startup fails softly and
/// <see cref="Available"/> stays false so tests skip instead of erroring.
/// </summary>
public sealed class NatsContainerFixture : IAsyncLifetime
{
    private const string NatsImage = "nats:2.10";

    // Generous enough for a cold image pull on CI, bounded so a host without (Linux) Docker —
    // e.g. a Windows runner — fails fast to the skip path instead of hanging the run.
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);

    private NatsContainer? _container;

    /// <summary>True when the container started and tests can run.</summary>
    public bool Available { get; private set; }

    /// <summary>Why the container is unavailable (for the skip message), or null.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>NATS client URL for the running container.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        // Fast pre-check: on a runner without a Docker daemon (e.g. windows-latest) the Docker client's
        // connect can HANG well past StartupTimeout (the token isn't honored during the socket/pipe
        // connect), which is why the whole job timed out. Probing the endpoint first lets such a host
        // skip in milliseconds instead.
        if (!DockerEndpointPresent())
        {
            Available = false;
            SkipReason = "No Docker endpoint found (is Docker running with Linux containers?).";
            return;
        }

        try
        {
            // NatsBuilder.Build() validates the Docker endpoint and throws when it is unreachable,
            // so it lives here (inside the guard), not in a field initializer.
            using var cts = new CancellationTokenSource(StartupTimeout);
            _container = new NatsBuilder(NatsImage).Build();
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

    // Best-effort, fast check for a local Docker daemon, so the container start is only attempted when
    // one is likely present. Honors DOCKER_HOST; otherwise looks for the conventional socket/named pipe.
    private static bool DockerEndpointPresent()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST")))
        {
            return true;
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                return Directory.EnumerateFiles(@"\\.\pipe\")
                    .Any(pipe => pipe.Contains("docker_engine", StringComparison.OrdinalIgnoreCase));
            }
            catch (IOException)
            {
                return false;
            }
        }

        return File.Exists("/var/run/docker.sock");
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Skips the calling test when the container could not start.</summary>
    public void RequireContainer() => Assert.SkipUnless(Available, SkipReason ?? "NATS container unavailable.");

    /// <summary>A fresh connection wired with the Dasim.Radio serializer registry.</summary>
    public NatsConnection CreateConnection() => new(RadioNatsOpts.ForUrl(ConnectionString));
}
