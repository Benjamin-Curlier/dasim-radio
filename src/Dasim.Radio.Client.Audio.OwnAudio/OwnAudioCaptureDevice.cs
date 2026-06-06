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
/// </summary>
public sealed class OwnAudioCaptureDevice : IAudioCaptureDevice
{
    private readonly string? _deviceId;
    private readonly ILogger<OwnAudioCaptureDevice> _logger;
    private readonly PcmFrameAccumulator _accumulator;

    private IOwnAudioEngine? _engine;
    private CancellationTokenSource? _cts;
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

        _engine = OwnAudioEngineFactory.Create(config);
        _engine.Initialize(config);
        _engine.Start();

        _cts = new CancellationTokenSource();
        var thread = new Thread(() => ReadLoop(_cts.Token)) { IsBackground = true, Name = "dasim-capture" };
        thread.Start();
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
        _cts?.Cancel();
        try
        {
            _engine?.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OwnAudio capture stop failed.");
        }

        _engine?.Dispose();
        _cts?.Dispose();
    }

    private void ReadLoop(CancellationToken token)
    {
        int bufferSamples = Math.Max(_engine!.FramesPerBuffer, Format.SamplesPerChannel) * Format.Channels;
        float[] buffer = new float[bufferSamples];

        try
        {
            while (!token.IsCancellationRequested)
            {
                int received = _engine.Receives(buffer);
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

    private void OnFrame(ReadOnlySpan<short> frame) => FrameCaptured?.Invoke(frame);
}
