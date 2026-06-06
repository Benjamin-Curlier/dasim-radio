using Dasim.Radio.Audio;
using Microsoft.Extensions.Logging;
using OwnAudioConfig = Ownaudio.Core.AudioConfig;
using OwnAudioEngineFactory = Ownaudio.Core.AudioEngineFactory;

namespace Dasim.Radio.Client.Audio.OwnAudio;

/// <summary>
/// Lists capture/playback devices via OwnAudioSharp. Build-only: it touches the native engine, so it
/// can't run in headless CI. Each call spins up and initializes a short-lived engine (device discovery
/// happens during native host initialization) and tears it down — acceptable for an occasional, one-shot
/// device listing.
/// </summary>
public sealed class OwnAudioDeviceEnumerator(ILogger<OwnAudioDeviceEnumerator> logger) : IAudioDeviceEnumerator
{
    private readonly ILogger<OwnAudioDeviceEnumerator> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public IReadOnlyList<AudioDeviceInfo> GetDevices(AudioDeviceDirection direction)
    {
        using var engine = OwnAudioEngineFactory.Create(OwnAudioConfig.Default);

        int initResult = engine.Initialize(OwnAudioConfig.Default);
        if (initResult != 0)
        {
            _logger.LogWarning("OwnAudio engine failed to initialize for enumeration (code {Code}).", initResult);
            return [];
        }

        IEnumerable<Ownaudio.Core.AudioDeviceInfo> native =
            direction == AudioDeviceDirection.Capture ? engine.GetInputDevices() : engine.GetOutputDevices();

        // The native DeviceId round-trips back as AudioConfig.InputDeviceId/OutputDeviceId for selection.
        return [.. native.Select(d => new AudioDeviceInfo(d.DeviceId, d.Name, direction, d.IsDefault))];
    }
}
