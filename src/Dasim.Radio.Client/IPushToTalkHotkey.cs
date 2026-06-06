namespace Dasim.Radio.Client;

/// <summary>
/// The push-to-talk input seam. Platform providers (SharpHook on Windows, evdev on Linux — see
/// <c>docs/wayland-ptt-spike.md</c>) and the on-screen button implement this; the engine depends only
/// on the seam. <see cref="Pressed"/>/<see cref="Released"/> may be raised on any thread.
/// </summary>
public interface IPushToTalkHotkey : IDisposable
{
    /// <summary>Raised when the PTT key/button goes down.</summary>
    event Action? Pressed;

    /// <summary>Raised when the PTT key/button goes up.</summary>
    event Action? Released;

    /// <summary>Begins listening for the hotkey.</summary>
    void Start();

    /// <summary>Stops listening for the hotkey.</summary>
    void Stop();
}

/// <summary>
/// A push-to-talk source driven explicitly by code — backs the on-screen PTT button and is the
/// universal fallback when no global-hotkey provider is available (e.g. a locked-down Wayland box).
/// </summary>
public sealed class ManualPushToTalk : IPushToTalkHotkey
{
    public event Action? Pressed;

    public event Action? Released;

    /// <summary>Signals a PTT press.</summary>
    public void Press() => Pressed?.Invoke();

    /// <summary>Signals a PTT release.</summary>
    public void Release() => Released?.Invoke();

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}
