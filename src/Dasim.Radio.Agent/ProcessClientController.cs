using Dasim.Radio.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dasim.Radio.Agent;

/// <summary>
/// Drives the single client process via an <see cref="IProcessRunner"/>. Every state transition and
/// every read runs under one <see cref="Lock"/>; the process operations themselves are synchronous, so
/// a lock (not an async gate) is the simplest correct guard. This is the Agent's analogue of the
/// floor authority's "serialize the side effects" rule.
/// <para>
/// The process operations run to completion under the lock, so the <c>CancellationToken</c> on the
/// command methods is accepted for interface symmetry (and a future async client handshake) but is not
/// observed by this synchronous implementation. Host shutdown does not route through these methods —
/// it goes through <see cref="Dispose"/>, which only releases the OS handle.
/// </para>
/// </summary>
public sealed class ProcessClientController : IClientController, IDisposable
{
    private readonly IProcessRunner _runner;
    private readonly AgentOptions _options;
    private readonly ILogger<ProcessClientController> _logger;
    private readonly Lock _gate = new();

    private IProcessHandle? _handle;
    private string? _currentConfigId;

    public ProcessClientController(
        IProcessRunner runner, IOptions<AgentOptions> options, ILogger<ProcessClientController> logger)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _runner = runner;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return IsRunningCore();
            }
        }
    }

    public string? CurrentConfigId
    {
        get
        {
            lock (_gate)
            {
                return IsRunningCore() ? _currentConfigId : null;
            }
        }
    }

    public ValueTask<AgentCommandResult> LaunchAsync(string configId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configId);

        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(_options.ClientExecutablePath))
            {
                return Result("Agent:ClientExecutablePath is not configured.");
            }

            if (IsRunningCore() && !_options.AllowReplaceRunningClient)
            {
                return Result("A client is already running; stop it first.");
            }

            return new ValueTask<AgentCommandResult>(LaunchCore(configId));
        }
    }

    public ValueTask<AgentCommandResult> StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            StopCore();
        }

        return new ValueTask<AgentCommandResult>(new AgentCommandResult(true));
    }

    public ValueTask<AgentCommandResult> ReconfigureAsync(string configId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configId);

        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(_options.ClientExecutablePath))
            {
                return Result("Agent:ClientExecutablePath is not configured.");
            }

            // Reconfigure is always a replace: stop the current client (if any) then relaunch — atomic
            // under the lock so a heartbeat never sees the transient "stopped" gap as a crash.
            return new ValueTask<AgentCommandResult>(LaunchCore(configId));
        }
    }

    public void Dispose()
    {
        // The agent stopping does NOT terminate the client — an operator should stay on the net across an
        // agent restart. We only release our OS handle; supervision/orphan-reclaim is a follow-up.
        lock (_gate)
        {
            _handle?.Dispose();
            _handle = null;
            _currentConfigId = null;
        }
    }

    private bool IsRunningCore() => _handle is { HasExited: false };

    private AgentCommandResult LaunchCore(string configId)
    {
        StopCore();
        try
        {
            _handle = _runner.Start(_options.ClientExecutablePath, configId);
            _currentConfigId = configId;
            _logger.LogInformation("Launched client with config '{ConfigId}'.", configId);
            return new AgentCommandResult(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to launch client with config '{ConfigId}'.", configId);
            _handle = null;
            _currentConfigId = null;
            return new AgentCommandResult(false, $"Failed to launch client: {ex.Message}");
        }
    }

    private void StopCore()
    {
        IProcessHandle? handle = _handle;
        _handle = null;
        _currentConfigId = null;
        if (handle is null)
        {
            return;
        }

        try
        {
            if (!handle.HasExited)
            {
                handle.Kill();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to kill the client process; disposing the handle anyway.");
        }
        finally
        {
            handle.Dispose();
        }
    }

    private static ValueTask<AgentCommandResult> Result(string error) =>
        new(new AgentCommandResult(false, error));
}
