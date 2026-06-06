using Microsoft.Extensions.Logging;

namespace Dasim.Radio.Client.Ptt;

/// <summary>
/// Global push-to-talk by reading a Linux input device (<c>/dev/input/event*</c>) directly — the path
/// that works on both X11 and Wayland (it bypasses the display server). Requires read access to the
/// device (the <c>input</c> group or a udev rule). Build-only: it can't run in headless CI, and the
/// device read is untestable; the parsing/edge logic it uses (<see cref="EvdevInputEventParser"/> +
/// <see cref="PttKeyState"/>) is unit-tested in <c>Dasim.Radio.Client</c>.
/// </summary>
public sealed class EvdevPushToTalk : IPushToTalkHotkey
{
    private readonly string _devicePath;
    private readonly PttKeyState _keyState;
    private readonly ILogger<EvdevPushToTalk> _logger;

    private FileStream? _stream;
    private CancellationTokenSource? _cts;
    private bool _disposed;

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
        _stream = new FileStream(_devicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoopAsync(_cts.Token), CancellationToken.None);
        _logger.LogInformation("Reading evdev PTT (key {Key}) from {Device}.", _keyState.KeyCode, _devicePath);
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
        _stream?.Dispose(); // unblocks a read blocked in the device
        _cts?.Dispose();
    }

    private async Task ReadLoopAsync(CancellationToken token)
    {
        byte[] record = new byte[EvdevInputEventParser.RecordSize];
        try
        {
            while (!token.IsCancellationRequested)
            {
                await _stream!.ReadExactlyAsync(record, token).ConfigureAwait(false);
                if (!EvdevInputEventParser.TryParse(record, out EvdevInputEvent input))
                {
                    continue;
                }

                switch (_keyState.Apply(input))
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
        catch (Exception ex) when (token.IsCancellationRequested)
        {
            // Normal shutdown: disposing the stream interrupts the blocked read.
            _ = ex;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "evdev read loop for {Device} faulted.", _devicePath);
        }
    }
}
