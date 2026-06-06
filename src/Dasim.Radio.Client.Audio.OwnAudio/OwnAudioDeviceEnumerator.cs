using Dasim.Radio.Audio;
using OwnAudioConfig = Ownaudio.Core.AudioConfig;
using OwnAudioEngineFactory = Ownaudio.Core.AudioEngineFactory;

namespace Dasim.Radio.Client.Audio.OwnAudio;

/// <summary>
/// Lists capture/playback devices via OwnAudioSharp. Build-only: it touches the native engine, so it
/// can't run in headless CI.
/// </summary>
public sealed class OwnAudioDeviceEnumerator : IAudioDeviceEnumerator
{
    public IReadOnlyList<AudioDeviceInfo> GetDevices(AudioDeviceDirection direction)
    {
        using var engine = OwnAudioEngineFactory.Create(OwnAudioConfig.Default);

        IEnumerable<Ownaudio.Core.AudioDeviceInfo> native =
            direction == AudioDeviceDirection.Capture ? engine.GetInputDevices() : engine.GetOutputDevices();

        return [.. native.Select(d => new AudioDeviceInfo(d.DeviceId, d.Name, direction, d.IsDefault))];
    }
}
