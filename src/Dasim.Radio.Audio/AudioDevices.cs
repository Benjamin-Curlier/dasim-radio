namespace Dasim.Radio.Audio;

// Capture/playback is a client-only concern (the media service has no devices — it mixes NATS
// streams; the manager has no audio at all). These are the abstractions; the OwnAudioSharp
// implementation lands with the client.

/// <summary>Direction of an audio endpoint.</summary>
public enum AudioDeviceDirection
{
    Capture,
    Playback,
}

/// <summary>A capture or playback device discovered on the host.</summary>
public sealed record AudioDeviceInfo(
    string Id,
    string Name,
    AudioDeviceDirection Direction,
    bool IsDefault);

/// <summary>
/// Receives one captured frame of interleaved 16-bit PCM. The span is valid only for the
/// duration of the call — copy out anything you need to keep; do not store the span.
/// </summary>
public delegate void AudioFrameHandler(ReadOnlySpan<short> frame);

/// <summary>Lists the host's capture and playback devices.</summary>
public interface IAudioDeviceEnumerator
{
    /// <summary>Returns the devices available for the given <paramref name="direction"/>.</summary>
    IReadOnlyList<AudioDeviceInfo> GetDevices(AudioDeviceDirection direction);
}

/// <summary>
/// Captures microphone audio as fixed-size PCM frames in <see cref="Format"/>, raising
/// <see cref="FrameCaptured"/> on a real-time audio thread. Keep the handler allocation-free and
/// non-blocking.
/// </summary>
public interface IAudioCaptureDevice : IDisposable
{
    /// <summary>The format of the captured frames.</summary>
    AudioFormat Format { get; }

    /// <summary>Raised once per captured frame on the audio thread.</summary>
    event AudioFrameHandler? FrameCaptured;

    /// <summary>Begins capturing.</summary>
    void Start();

    /// <summary>Stops capturing.</summary>
    void Stop();
}

/// <summary>
/// Plays decoded PCM frames in <see cref="Format"/>. <see cref="Submit"/> copies the frame into
/// the device's own buffer, so the caller may reuse its span immediately.
/// </summary>
public interface IAudioPlaybackDevice : IDisposable
{
    /// <summary>The format of the submitted frames.</summary>
    AudioFormat Format { get; }

    /// <summary>Enqueues one frame of interleaved 16-bit PCM for playback.</summary>
    void Submit(ReadOnlySpan<short> pcm);

    /// <summary>Begins playback.</summary>
    void Start();

    /// <summary>Stops playback and discards anything still queued.</summary>
    void Stop();
}
