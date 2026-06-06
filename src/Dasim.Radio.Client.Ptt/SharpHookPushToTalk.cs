using Microsoft.Extensions.Logging;
using SharpHook;
using SharpHook.Data;

namespace Dasim.Radio.Client.Ptt;

/// <summary>
/// Global push-to-talk via a SharpHook (libuiohook) keyboard hook — the path for Windows and X11
/// (see <c>docs/wayland-ptt-spike.md</c>; libuiohook does not capture unfocused keys on Wayland).
/// Build-only: it needs the native hook and a display, so it can't run in headless CI.
/// <para>
/// <b>Single-use:</b> a SharpHook hook cannot be restarted once disposed, so <see cref="Stop"/> is
/// terminal — create a new instance to resume (unlike the reversible <c>ManualPushToTalk</c>).
/// </para>
/// </summary>
public sealed class SharpHookPushToTalk : IPushToTalkHotkey
{
    private readonly KeyCode _pttKey;
    private readonly ILogger<SharpHookPushToTalk> _logger;
    private readonly IGlobalHook _hook;
    private bool _running;
    private bool _down;
    private int _disposed;

    public SharpHookPushToTalk(KeyCode pttKey, ILogger<SharpHookPushToTalk> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _pttKey = pttKey;
        _logger = logger;
        _hook = new EventLoopGlobalHook(GlobalHookType.Keyboard);
    }

    public event Action? Pressed;

    public event Action? Released;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_running)
        {
            return; // already listening
        }

        _running = true;
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;

        // RunAsync runs the native loop on its own thread; observe faults so they aren't lost.
        _ = _hook.RunAsync().ContinueWith(
            task => _logger.LogError(task.Exception, "Global PTT hook faulted."),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

        _logger.LogInformation("Global PTT hook listening for {Key}.", _pttKey);
    }

    public void Stop() => Dispose();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        if (_running)
        {
            _hook.KeyPressed -= OnKeyPressed;
            _hook.KeyReleased -= OnKeyReleased;
            _running = false;
        }

        _hook.Dispose(); // stops the RunAsync loop
    }

    // Handlers run on the hook's single event-loop thread; libuiohook repeats KeyPressed while held, so
    // the _down guard collapses a hold into one press + one release.
    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode == _pttKey && !_down)
        {
            _down = true;
            Pressed?.Invoke();
        }
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode == _pttKey && _down)
        {
            _down = false;
            Released?.Invoke();
        }
    }
}
