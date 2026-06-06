using Dasim.Radio.Audio;
using Microsoft.Extensions.Logging;
using IOwnAudioEngine = Ownaudio.Core.IAudioEngine;
using OwnAudioConfig = Ownaudio.Core.AudioConfig;
using OwnAudioEngineFactory = Ownaudio.Core.AudioEngineFactory;

namespace Dasim.Radio.Client.Audio.OwnAudio;

/// <summary>
/// Captures microphone audio via OwnAudioSharp, reframing the engine's float buffers into the radio's
/// fixed 20 ms 16-bit frames (<see cref="PcmFrameAccumulator"/>) and raising <see cref="FrameCaptured"/>
/// on a dedicated read thread. Build-only (needs the native engine + a real device); <b>single-use</b>.
/// <para><b><see cref="Start"/> blocks</b> while the native engine initializes (tens of ms to several
/// seconds) — call it off the UI thread.</para>
/// </summary>
public sealed class OwnAudioCaptureDevice : IAudioCaptureDevice
{
    private readonly string? _deviceId;
    private readonly ILogger<OwnAudioCaptureDevice> _logger;
    private readonly PcmFrameAccumulator _accumulator;

    private IOwnAudioEngine? _engine;
    private CancellationTokenSource? _cts;
    private Thread? _readThread;
    private bool _disposed;

    public OwnAudioCaptureDevice(string? deviceId, ILogger<OwnAudioCaptureDevice> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _deviceId = deviceId;
        _logger = logger;
        _accumulator = new PcmFrameAccumulator(Format.SamplesPerFrame);
    }

    public AudioFormat Format => AudioFormat.Voice;

    public event AudioFrameHandler? FrameCaptured;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_engine is not null)
        {
            return;
        }

        var config = new OwnAudioConfig
        {
            SampleRate = Format.SampleRateHz,
            Channels = Format.Channels,
            EnableInput = true,
            EnableOutput = false,
        };
        if (!string.IsNullOrWhiteSpace(_deviceId))
        {
            config.InputDeviceId = _deviceId;
        }

        IOwnAudioEngine engine = OwnAudioEngineFactory.Create(config);
        int initResult = engine.Initialize(config);
        if (initResult != 0)
        {
            engine.Dispose();
            throw new InvalidOperationException($"OwnAudio capture engine failed to initialize (code {initResult}).");
        }

        int startResult = engine.Start();
        if (startResult != 0)
        {
            _logger.LogWarning("OwnAudio capture engine Start returned {Code}.", startResult);
        }

        _engine = engine;
        _cts = new CancellationTokenSource();
        _readThread = new Thread(() => ReadLoop(engine, _cts.Token)) { IsBackground = true, Name = "dasim-capture" };
        _readThread.Start();
        _logger.LogInformation("OwnAudio capture started ({Device}).", _deviceId ?? "default");
    }

    public void Stop() => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Order matters: cancel so the loop will exit on its next check, Stop to unblock a read currently
        // waiting on the device, then JOIN the read thread before disposing the engine — otherwise the
        // thread could dereference a disposed native engine and crash the process.
        _cts?.Cancel();
        try
        {
            _engine?.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OwnAudio capture stop failed.");
        }

        if (_readThread is not null && !_readThread.Join(TimeSpan.FromSeconds(3)))
        {
            _logger.LogWarning("OwnAudio capture read thread did not stop within the timeout.");
        }

        _engine?.Dispose();
        _engine = null;
        _cts?.Dispose();
    }

    private void ReadLoop(IOwnAudioEngine engine, CancellationToken token)
    {
        int bufferSamples = Math.Max(engine.FramesPerBuffer, Format.SamplesPerChannel) * Format.Channels;
        float[] buffer = new float[bufferSamples];

        try
        {
            while (!token.IsCancellationRequested)
            {
                int received = engine.Receives(buffer);
                if (received > 0)
                {
                    _accumulator.Append(buffer.AsSpan(0, received), OnFrame);
                }
            }
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            _logger.LogError(ex, "OwnAudio capture loop faulted.");
        }
    }

    // A faulty subscriber must not kill capture — log and keep going.
    private void OnFrame(ReadOnlySpan<short> frame)
    {
        try
        {
            FrameCaptured?.Invoke(frame);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A captured-frame handler threw.");
        }
    }
}
