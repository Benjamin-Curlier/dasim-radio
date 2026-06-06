using Microsoft.Extensions.Logging;

namespace Dasim.Radio.Client.Ptt;

/// <summary>
/// Global push-to-talk by reading a Linux input device (<c>/dev/input/event*</c>) directly — the path
/// that works on both X11 and Wayland (it bypasses the display server). Requires read access to the
/// device (the <c>input</c> group or a udev rule). Build-only: it can't run in headless CI, and the
/// device read is untestable; the parsing/edge logic it uses (<see cref="EvdevInputEventParser"/> +
/// <see cref="PttKeyState"/>) is unit-tested in <c>Dasim.Radio.Client</c>. <b>Single-use:</b>
/// <see cref="Stop"/> is terminal — create a new instance to resume.
/// </summary>
public sealed class EvdevPushToTalk : IPushToTalkHotkey
{
    private static readonly TimeSpan ReopenDelay = TimeSpan.FromSeconds(1);

    private readonly string _devicePath;
    private readonly PttKeyState _keyState;
    private readonly ILogger<EvdevPushToTalk> _logger;

    private CancellationTokenSource? _cts;
    private bool _started;
    private int _disposed;

    public EvdevPushToTalk(string devicePath, ushort keyCode, ILogger<EvdevPushToTalk> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        ArgumentNullException.ThrowIfNull(logger);
        _devicePath = devicePath;
        _keyState = new PttKeyState(keyCode);
        _logger = logger;
    }

    public event Action? Pressed;

    public event Action? Released;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_started)
        {
            return;
        }

        _started = true;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoopAsync(_cts.Token), CancellationToken.None);
        _logger.LogInformation("Reading evdev PTT (key {Key}) from {Device}.", _keyState.KeyCode, _devicePath);
    }

    public void Stop() => Dispose();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
    }

    private async Task ReadLoopAsync(CancellationToken token)
    {
        byte[] record = new byte[EvdevInputEventParser.RecordSize];

        // Reopen on fault so a hot-swapped/unplugged keyboard (a real field scenario) doesn't kill PTT
        // permanently — mirrors the media service's resilient resubscribe loop.
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var stream = new FileStream(
                    _devicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                // Disposing the device interrupts a read blocked in the kernel when cancellation fires.
                await using CancellationTokenRegistration registration =
                    token.Register(static s => ((FileStream)s!).Dispose(), stream);

                while (!token.IsCancellationRequested)
                {
                    await stream.ReadExactlyAsync(record, token).ConfigureAwait(false);
                    if (EvdevInputEventParser.TryParse(record, out EvdevInputEvent input))
                    {
                        Raise(_keyState.Apply(input));
                    }
                }
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                return; // normal shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "evdev read for {Device} faulted; reopening.", _devicePath);
                try
                {
                    await Task.Delay(ReopenDelay, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void Raise(PttEdge edge)
    {
        switch (edge)
        {
            case PttEdge.Pressed:
                Pressed?.Invoke();
                break;
            case PttEdge.Released:
                Released?.Invoke();
                break;
            default:
                break;
        }
    }
}
