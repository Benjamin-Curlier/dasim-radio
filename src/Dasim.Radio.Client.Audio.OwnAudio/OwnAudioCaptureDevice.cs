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
/// <para>Opens the device on the read thread and <b>reopens it after a fault</b> (unplug / hot-swap /
/// driver hiccup) with a short backoff, so a mid-stream device failure doesn't silently kill capture for
/// the rest of the process — mirroring <c>EvdevPushToTalk</c>. <see cref="Start"/> returns immediately;
/// capture begins once the device is available. <b>Single-use:</b> <see cref="Stop"/> is terminal.</para>
/// </summary>
public sealed class OwnAudioCaptureDevice : IAudioCaptureDevice
{
    private static readonly TimeSpan ReopenDelay = TimeSpan.FromSeconds(1);

    private readonly string? _deviceId;
    private readonly ILogger<OwnAudioCaptureDevice> _logger;
    private readonly PcmFrameAccumulator _accumulator;

    private CancellationTokenSource? _cts;
    private Thread? _readThread;
    private bool _started;
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
        if (_started)
        {
            return;
        }

        _started = true;
        _cts = new CancellationTokenSource();
        _readThread = new Thread(() => ReadLoop(_cts.Token)) { IsBackground = true, Name = "dasim-capture" };
        _readThread.Start();
        _logger.LogInformation("OwnAudio capture starting ({Device}).", _deviceId ?? "default");
    }

    public void Stop() => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Cancel: the read thread's cancellation registration Stops the live engine to unblock a Receives
        // blocked on the device, and the read thread owns the engine's teardown. Join before disposing the
        // CTS so the thread has finished touching the native engine and can't dereference a disposed one.
        _cts?.Cancel();
        if (_readThread is not null && !_readThread.Join(TimeSpan.FromSeconds(3)))
        {
            _logger.LogWarning("OwnAudio capture read thread did not stop within the timeout.");
        }

        _cts?.Dispose();
    }

    private void ReadLoop(CancellationToken token)
    {
        // Open the device, read until it faults, then reopen after a short backoff so a device that is
        // unplugged / hot-swapped, or whose driver hiccups mid-stream, doesn't kill capture for the rest of
        // the process. Mirrors EvdevPushToTalk.ReadLoopAsync and the media service's resubscribe loop.
        while (!token.IsCancellationRequested)
        {
            try
            {
                IOwnAudioEngine engine = CreateEngine();
                try
                {
                    // Stopping the engine on cancellation unblocks a Receives() blocked on the device (the
                    // role the evdev loop's stream-dispose registration plays). Disposing the registration
                    // when the block exits waits for any in-flight Stop callback to finish, so the engine is
                    // never Stopped on the canceller's thread while being disposed on this one.
                    using (token.Register(static s => SafeStop((IOwnAudioEngine)s!), engine))
                    {
                        int bufferSamples = Math.Max(engine.FramesPerBuffer, Format.SamplesPerChannel) * Format.Channels;
                        float[] buffer = new float[bufferSamples];
                        while (!token.IsCancellationRequested)
                        {
                            int received = engine.Receives(buffer);
                            if (received > 0)
                            {
                                _accumulator.Append(buffer.AsSpan(0, received), OnFrame);
                            }
                        }
                    }
                }
                finally
                {
                    SafeStop(engine);
                    engine.Dispose();
                }
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                return; // normal shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OwnAudio capture faulted; reopening.");
                if (token.WaitHandle.WaitOne(ReopenDelay))
                {
                    return;
                }
            }
        }
    }

    private IOwnAudioEngine CreateEngine()
    {
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

        return engine;
    }

    private static void SafeStop(IOwnAudioEngine engine)
    {
        try
        {
            engine.Stop();
        }
        catch (Exception)
        {
            // Best-effort: Stop only unblocks a pending read / quiesces before dispose; a failure here must
            // not crash the read thread or mask the real teardown.
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
