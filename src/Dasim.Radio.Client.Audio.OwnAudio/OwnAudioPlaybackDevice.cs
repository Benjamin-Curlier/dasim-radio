using Dasim.Radio.Audio;
using Microsoft.Extensions.Logging;
using IOwnAudioEngine = Ownaudio.Core.IAudioEngine;
using OwnAudioConfig = Ownaudio.Core.AudioConfig;
using OwnAudioEngineFactory = Ownaudio.Core.AudioEngineFactory;

namespace Dasim.Radio.Client.Audio.OwnAudio;

/// <summary>
/// Plays decoded PCM via OwnAudioSharp, converting the radio's 16-bit frames to float and sending them
/// to the engine. Build-only (needs the native engine + a real device); <b>single-use</b>.
/// </summary>
public sealed class OwnAudioPlaybackDevice : IAudioPlaybackDevice
{
    private readonly string? _deviceId;
    private readonly ILogger<OwnAudioPlaybackDevice> _logger;
    private float[] _scratch;

    private IOwnAudioEngine? _engine;
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

        _engine = OwnAudioEngineFactory.Create(config);
        _engine.Initialize(config);
        _engine.Start();
        _logger.LogInformation("OwnAudio playback started ({Device}).", _deviceId ?? "default");
    }

    public void Submit(ReadOnlySpan<short> pcm)
    {
        IOwnAudioEngine? engine = _engine;
        if (engine is null || pcm.IsEmpty)
        {
            return;
        }

        if (_scratch.Length < pcm.Length)
        {
            _scratch = new float[pcm.Length];
        }

        Span<float> floats = _scratch.AsSpan(0, pcm.Length);
        PcmConvert.ShortsToFloats(pcm, floats);
        engine.Send(floats);
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
    }
}
