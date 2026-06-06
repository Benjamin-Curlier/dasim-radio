using Avalonia.Threading;
using Dasim.Radio.Audio;
using Microsoft.Extensions.Logging;

namespace Dasim.Radio.Client.App.ViewModels;

/// <summary>
/// Binds the headless <see cref="RadioClientEngine"/> to the window: surfaces the floor/transmit state,
/// drives the on-screen push-to-talk, and lists the host's audio devices. Engine state changes arrive on
/// the engine's control thread and are marshalled to the UI thread before raising change notifications.
/// </summary>
public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly RadioClientEngine _engine;
    private readonly ManualPushToTalk _onScreenPtt;
    private bool _disposed;

    public MainViewModel(
        RadioClientEngine engine,
        ManualPushToTalk onScreenPtt,
        IAudioDeviceEnumerator devices,
        ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(onScreenPtt);
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(logger);
        _engine = engine;
        _onScreenPtt = onScreenPtt;

        CaptureDevices = SafeList(devices, AudioDeviceDirection.Capture, logger);
        PlaybackDevices = SafeList(devices, AudioDeviceDirection.Playback, logger);

        _engine.StateChanged += OnStateChanged;
    }

    public IReadOnlyList<string> CaptureDevices { get; }

    public IReadOnlyList<string> PlaybackDevices { get; }

    public string PhaseText => _engine.State.Phase.ToString();

    public bool IsTransmitting => _engine.State.IsTransmitting;

    public string TransmitNetText => $"Net: {_engine.State.TransmitNetId}";

    public string HoldersText => _engine.State.FloorHolders.Count == 0
        ? "No nets"
        : string.Join("    ", _engine.State.FloorHolders.Select(kv => $"{kv.Key}: {kv.Value ?? "idle"}"));

    /// <summary>Called on PTT-button pointer-down — press the floor.</summary>
    public void PushToTalkDown() => _onScreenPtt.Press();

    /// <summary>Called on PTT-button pointer-up — release the floor.</summary>
    public void PushToTalkUp() => _onScreenPtt.Release();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _engine.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(object? sender, ClientRadioState state) =>
        Dispatcher.UIThread.Post(() =>
        {
            RaisePropertyChanged(nameof(PhaseText));
            RaisePropertyChanged(nameof(IsTransmitting));
            RaisePropertyChanged(nameof(TransmitNetText));
            RaisePropertyChanged(nameof(HoldersText));
        });

    private static IReadOnlyList<string> SafeList(
        IAudioDeviceEnumerator devices, AudioDeviceDirection direction, ILogger logger)
    {
        try
        {
            return [.. devices.GetDevices(direction).Select(d => d.Name + (d.IsDefault ? " (default)" : string.Empty))];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enumerate {Direction} devices.", direction);
            return [];
        }
    }
}
