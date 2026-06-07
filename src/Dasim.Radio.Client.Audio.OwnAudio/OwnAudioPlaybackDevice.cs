using System.Diagnostics;
using Dasim.Radio.Audio;
using Microsoft.Extensions.Logging;
using IOwnAudioEngine = Ownaudio.Core.IAudioEngine;
using OwnAudioConfig = Ownaudio.Core.AudioConfig;
using OwnAudioEngineFactory = Ownaudio.Core.AudioEngineFactory;

namespace Dasim.Radio.Client.Audio.OwnAudio;

/// <summary>
/// Plays decoded PCM via OwnAudioSharp, converting the radio's 16-bit frames to float and sending them
/// to the engine. Build-only (needs the native engine + a real device); <b>single-use</b>.
/// <para><b><see cref="Start"/> blocks</b> while the native engine initializes (tens of ms to several
/// seconds) — call it off the UI thread. <see cref="Submit"/> is not thread-safe; call it from a single
/// pump (the receive loop).</para>
/// </summary>
public sealed class OwnAudioPlaybackDevice : IAudioPlaybackDevice
{
    private static readonly TimeSpan ReopenBackoff = TimeSpan.FromSeconds(1);

    private readonly string? _deviceId;
    private readonly ILogger<OwnAudioPlaybackDevice> _logger;
    private float[] _scratch;

    private IOwnAudioEngine? _engine;
    private long _nextOpenTimestamp; // Stopwatch ticks; gates reopen attempts after a fault (monotonic)
    private bool _disposed;

    public OwnAudioPlaybackDevice(string? deviceId, ILogger<OwnAudioPlaybackDevice> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _deviceId = deviceId;
        _logger = logger;
        _scratch = new float[Format.SamplesPerFrame];
    }

    public AudioFormat Format => AudioFormat.Voice;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_engine is not null)
        {
            return;
        }

        _engine = CreateEngine();
        _logger.LogInformation("OwnAudio playback started ({Device}).", _deviceId ?? "default");
    }

    public void Submit(ReadOnlySpan<short> pcm)
    {
        if (_disposed || pcm.IsEmpty)
        {
            return;
        }

        IOwnAudioEngine? engine = EnsureEngine();
        if (engine is null)
        {
            return; // device unavailable / backing off after a fault — drop this frame
        }

        if (_scratch.Length < pcm.Length)
        {
            _scratch = new float[pcm.Length];
        }

        Span<float> floats = _scratch.AsSpan(0, pcm.Length);
        PcmConvert.ShortsToFloats(pcm, floats);
        try
        {
            engine.Send(floats);
        }
        catch (Exception ex)
        {
            // A device fault (unplug / hot-swap / driver error). Tear the faulted engine down and back off
            // so subsequent frames reopen it instead of re-throwing on every frame (a log flood) and never
            // recovering when the device returns.
            _logger.LogWarning(ex, "OwnAudio playback send faulted; reopening after backoff.");
            FaultEngine();
        }
    }

    // Returns a live engine, lazily reopening one after a fault once the backoff has elapsed. Called only
    // from Submit (the single receive pump), so no synchronization is needed.
    private IOwnAudioEngine? EnsureEngine()
    {
        if (_engine is not null)
        {
            return _engine;
        }

        if (Stopwatch.GetTimestamp() < _nextOpenTimestamp)
        {
            return null; // still backing off
        }

        try
        {
            _engine = CreateEngine();
            _logger.LogInformation("OwnAudio playback reopened ({Device}).", _deviceId ?? "default");
            return _engine;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OwnAudio playback reopen failed; will retry.");
            StartBackoff();
            return null;
        }
    }

    private void FaultEngine()
    {
        IOwnAudioEngine? engine = _engine;
        _engine = null;
        StartBackoff();
        if (engine is null)
        {
            return;
        }

        try
        {
            engine.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OwnAudio playback stop failed.");
        }

        engine.Dispose();
    }

    private void StartBackoff() =>
        _nextOpenTimestamp = Stopwatch.GetTimestamp() + (long)(ReopenBackoff.TotalSeconds * Stopwatch.Frequency);

    private IOwnAudioEngine CreateEngine()
    {
        var config = new OwnAudioConfig
        {
            SampleRate = Format.SampleRateHz,
            Channels = Format.Channels,
            EnableInput = false,
            EnableOutput = true,
        };
        if (!string.IsNullOrWhiteSpace(_deviceId))
        {
            config.OutputDeviceId = _deviceId;
        }

        IOwnAudioEngine engine = OwnAudioEngineFactory.Create(config);
        int initResult = engine.Initialize(config);
        if (initResult != 0)
        {
            engine.Dispose();
            throw new InvalidOperationException($"OwnAudio playback engine failed to initialize (code {initResult}).");
        }

        int startResult = engine.Start();
        if (startResult != 0)
        {
            _logger.LogWarning("OwnAudio playback engine Start returned {Code}.", startResult);
        }

        return engine;
    }

    public void Stop() => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _engine?.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OwnAudio playback stop failed.");
        }

        _engine?.Dispose();
        _engine = null;
    }
}
