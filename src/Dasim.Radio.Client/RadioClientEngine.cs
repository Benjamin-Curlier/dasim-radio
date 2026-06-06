using System.Threading.Channels;
using Dasim.Radio.Audio;
using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.Audio;
using Dasim.Radio.Messaging.Floor;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dasim.Radio.Client;

/// <summary>
/// The headless radio client. Wires push-to-talk to the authoritative floor, transmits captured audio
/// only while it holds the floor, and plays back its per-listener mix. All control inputs (PTT and
/// floor events) funnel through ONE serialized loop — the client-side analogue of the media service's
/// single-consumer floor authority — so the state machine is touched from a single thread and the
/// async floor effects never race. Capture/encode and decode/playback run on their own pumps.
/// </summary>
public sealed class RadioClientEngine : IAsyncDisposable
{
    private readonly IAudioBus _bus;
    private readonly IFloorSignal _floor;
    private readonly IOpusEncoderFactory _encoderFactory;
    private readonly IOpusDecoderFactory _decoderFactory;
    private readonly IAudioCaptureDevice _capture;
    private readonly IAudioPlaybackDevice _playback;
    private readonly IPushToTalkHotkey _hotkey;
    private readonly ClientOptions _options;
    private readonly ILogger<RadioClientEngine> _logger;

    private readonly AudioFormat _format = AudioFormat.Voice;
    private readonly FloorStateMachine _machine = new();
    private readonly FramePool _pool;
    private readonly Channel<short[]> _captureChannel;
    private readonly Channel<ControlInput> _controlChannel;
    private readonly Dictionary<string, string?> _holders = [];
    private readonly string[] _listenNets;

    private const int CaptureQueueDepth = 4; // ~80 ms of transmit slack before the audio thread drops newest

    private readonly Lock _lifecycleGate = new();

    private volatile bool _transmitting;
    private volatile ClientRadioState _state;
    private long _droppedTransmitFrames;

    private CancellationTokenSource? _cts;
    private Task? _controlLoop;
    private Task? _transmitPump;
    private Task? _receivePump;
    private Task[] _floorSubscriptions = [];
    private LifecycleState _lifecycle = LifecycleState.Created;
    private bool _disposed;

    public RadioClientEngine(
        IAudioBus bus,
        IFloorSignal floor,
        IOpusEncoderFactory encoderFactory,
        IOpusDecoderFactory decoderFactory,
        IAudioCaptureDevice capture,
        IAudioPlaybackDevice playback,
        IPushToTalkHotkey hotkey,
        IOptions<ClientOptions> options,
        ILogger<RadioClientEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(floor);
        ArgumentNullException.ThrowIfNull(encoderFactory);
        ArgumentNullException.ThrowIfNull(decoderFactory);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(hotkey);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _bus = bus;
        _floor = floor;
        _encoderFactory = encoderFactory;
        _decoderFactory = decoderFactory;
        _capture = capture;
        _playback = playback;
        _hotkey = hotkey;
        _options = options.Value;
        _logger = logger;

        _pool = new FramePool(_format.SamplesPerFrame);
        _pool.Prewarm(CaptureQueueDepth + 1); // so the first talk-spurt is allocation-free, including the in-flight frame
        _captureChannel = Channel.CreateBounded<short[]>(new BoundedChannelOptions(CaptureQueueDepth)
        {
            FullMode = BoundedChannelFullMode.Wait, // paired with TryWrite, so a full channel drops the newest frame
            SingleReader = true,
            SingleWriter = true,
        });
        _controlChannel = Channel.CreateUnbounded<ControlInput>(new UnboundedChannelOptions { SingleReader = true });

        _listenNets = _options.ParentNetId is null
            ? [_options.OwnNetId]
            : [_options.OwnNetId, _options.ParentNetId];
        foreach (string net in _listenNets)
        {
            _holders[net] = null;
        }

        _state = CreateState();
    }

    /// <summary>The latest client state snapshot.</summary>
    public ClientRadioState State => _state;

    /// <summary>
    /// Count of captured frames dropped because the transmit pump couldn't keep up (e.g. a stalled
    /// publisher). Steady-state zero; a rising value signals back-pressure on the data plane.
    /// </summary>
    public long DroppedTransmitFrames => Interlocked.Read(ref _droppedTransmitFrames);

    /// <summary>Raised on every state transition with the new snapshot.</summary>
    public event EventHandler<ClientRadioState>? StateChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lifecycleGate)
        {
            switch (_lifecycle)
            {
                case LifecycleState.Running:
                    return Task.CompletedTask;
                case LifecycleState.Stopped:
                    throw new InvalidOperationException("The radio client engine cannot be restarted.");
                default:
                    _lifecycle = LifecycleState.Running;
                    break;
            }
        }

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        _hotkey.Pressed += OnPressed;
        _hotkey.Released += OnReleased;
        _capture.FrameCaptured += OnFrameCaptured;

        _controlLoop = Task.Run(() => ControlLoopAsync(token), CancellationToken.None);
        _transmitPump = Task.Run(() => TransmitLoopAsync(token), CancellationToken.None);
        _receivePump = Task.Run(() => ReceiveLoopAsync(token), CancellationToken.None);
        _floorSubscriptions = [.. _listenNets.Select(net => Task.Run(() => SubscribeFloorAsync(net, token), CancellationToken.None))];

        _playback.Start();
        _capture.Start();
        _hotkey.Start();

        _logger.LogInformation("Radio client '{ClientId}' started on net '{Net}'.", _options.ClientId, _options.OwnNetId);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleGate)
        {
            if (_lifecycle != LifecycleState.Running)
            {
                return; // idempotent: only the first Running→Stopped transition tears down
            }

            _lifecycle = LifecycleState.Stopped;
        }

        // Quiesce inputs first so nothing new enters the control loop or the capture channel.
        _capture.Stop();
        _capture.FrameCaptured -= OnFrameCaptured;
        _hotkey.Stop();
        _hotkey.Pressed -= OnPressed;
        _hotkey.Released -= OnReleased;

        // Ask the control loop to release any held/pending floor, then drain and finish it cleanly
        // (its token is still live, so the release actually goes out).
        _controlChannel.Writer.TryWrite(ControlInput.Ptt(pressed: false));
        _controlChannel.Writer.TryComplete();
        await SafeAwait(_controlLoop).ConfigureAwait(false);

        // Complete the capture channel and let the transmit pump drain it to completion FIRST, so every
        // queued pooled buffer is returned (cancelling mid-drain would strand them). Only then cancel
        // the token to unblock the receive/floor subscriptions, which hold no buffered state.
        _captureChannel.Writer.TryComplete();
        await SafeAwait(_transmitPump).ConfigureAwait(false);
        await (_cts?.CancelAsync() ?? Task.CompletedTask).ConfigureAwait(false);
        await SafeAwait(_receivePump).ConfigureAwait(false);
        await Task.WhenAll(_floorSubscriptions.Select(SafeAwait)).ConfigureAwait(false);

        _playback.Stop();
        _cts?.Dispose();
        _cts = null;
        _logger.LogInformation("Radio client '{ClientId}' stopped.", _options.ClientId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
    }

    // --- Audio thread ---------------------------------------------------------------------------

    private void OnFrameCaptured(ReadOnlySpan<short> frame)
    {
        if (!_transmitting)
        {
            return; // not holding the floor — drop captured audio without touching the pool
        }

        short[] buffer = _pool.Rent();
        frame.CopyTo(buffer);
        if (!_captureChannel.Writer.TryWrite(buffer))
        {
            // Channel full (pump stalled) — drop the newest frame, reclaim the buffer, and count it so a
            // sustained back-pressure stall is observable rather than silent.
            _pool.Return(buffer);
            Interlocked.Increment(ref _droppedTransmitFrames);
        }
    }

    private void OnPressed() => _controlChannel.Writer.TryWrite(ControlInput.Ptt(pressed: true));

    private void OnReleased() => _controlChannel.Writer.TryWrite(ControlInput.Ptt(pressed: false));

    // --- Control loop (single thread owns the state machine) ------------------------------------

    private async Task ControlLoopAsync(CancellationToken token)
    {
        try
        {
            await foreach (ControlInput input in _controlChannel.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                FloorEffect effect = Apply(input);
                await PerformAsync(effect, token).ConfigureAwait(false);
                _transmitting = _machine.IsTransmitting;
                PublishState();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private FloorEffect Apply(ControlInput input)
    {
        switch (input.Kind)
        {
            case ControlKind.PttPressed:
                return _machine.OnPttPressed();
            case ControlKind.PttReleased:
                return _machine.OnPttReleased();
            default:
                // Only track nets we actually listen to, so a stray net id can't accrete phantom keys.
                if (_holders.ContainsKey(input.NetId))
                {
                    _holders[input.NetId] = input.Holder;
                }

                return string.Equals(input.NetId, _options.OwnNetId, StringComparison.Ordinal)
                    ? _machine.OnFloor(input.Floor)
                    : FloorEffect.None;
        }
    }

    private async Task PerformAsync(FloorEffect effect, CancellationToken token)
    {
        try
        {
            switch (effect)
            {
                case FloorEffect.SendRequest:
                    await _floor.RequestAsync(
                        new FloorRequestMessage(_options.OwnNetId, _options.ParticipantId, _options.AdvertisedPriority),
                        token).ConfigureAwait(false);
                    break;
                case FloorEffect.SendRelease:
                    await _floor.ReleaseAsync(
                        new FloorReleaseMessage(_options.OwnNetId, _options.ParticipantId), token).ConfigureAwait(false);
                    break;
                default:
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to signal the floor ({Effect}).", effect);
        }
    }

    // --- Transmit pump (owns the encoder) -------------------------------------------------------

    private async Task TransmitLoopAsync(CancellationToken token)
    {
        using IOpusEncoder encoder = _encoderFactory.Create(_format, _options.EncoderSettings);

        // Reused across frames — safe ONLY because this pump is strictly sequential (it awaits each
        // publish before the next Encode) and the audio bus copies the payload into its write buffer
        // before PublishCapturedAsync completes. Do not fire-and-forget the publish, or frames will tear.
        byte[] output = new byte[OpusConstants.RecommendedMaxPacketBytes];

        try
        {
            await foreach (short[] frame in _captureChannel.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                try
                {
                    if (_transmitting)
                    {
                        int bytes = encoder.Encode(frame, output);
                        if (bytes > 0)
                        {
                            await _bus.PublishCapturedAsync(_options.ClientId, output.AsMemory(0, bytes), token)
                                .ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to encode/publish a captured frame.");
                }
                finally
                {
                    _pool.Return(frame);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    // --- Receive pump (owns the decoder) --------------------------------------------------------

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        using IOpusDecoder decoder = _decoderFactory.Create(_format);
        short[] pcm = new short[_format.SamplesPerFrame];

        try
        {
            await foreach (byte[] opus in _bus.SubscribeMixedAsync(_options.ClientId, token).ConfigureAwait(false))
            {
                try
                {
                    // The per-listener mix has no sequence number, so we decode what arrives; an empty
                    // payload is treated as a loss and concealed. (FEC/PLC over a sequence number is a
                    // Contracts + media-service follow-up.)
                    int samples = opus.Length == 0 ? decoder.DecodeLost(pcm) : decoder.Decode(opus, pcm);
                    _playback.Submit(pcm.AsSpan(0, samples * _format.Channels));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to decode/play a received frame.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    // --- Floor event subscriptions --------------------------------------------------------------

    private async Task SubscribeFloorAsync(string net, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await foreach (FloorEventMessage @event in _floor.SubscribeEventsAsync(net, token).ConfigureAwait(false))
                {
                    FloorInput input = FloorEventInterpreter.Interpret(@event, _options.ParticipantId);
                    _controlChannel.Writer.TryWrite(ControlInput.FloorEvent(net, input, @event.CurrentHolder));
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Floor subscription for net '{Net}' faulted; resubscribing.", net);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    // --- State snapshot -------------------------------------------------------------------------

    private void PublishState()
    {
        ClientRadioState snapshot = CreateState();
        _state = snapshot;
        StateChanged?.Invoke(this, snapshot);
    }

    private ClientRadioState CreateState() => new()
    {
        Phase = _machine.Phase,
        TransmitNetId = _options.OwnNetId,
        FloorHolders = new Dictionary<string, string?>(_holders, StringComparer.Ordinal),
    };

    private static async Task SafeAwait(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private enum LifecycleState
    {
        Created,
        Running,
        Stopped,
    }

    private enum ControlKind
    {
        PttPressed,
        PttReleased,
        Floor,
    }

    private readonly record struct ControlInput
    {
        public ControlKind Kind { get; private init; }

        public FloorInput Floor { get; private init; }

        public string NetId { get; private init; }

        public string? Holder { get; private init; }

        public static ControlInput Ptt(bool pressed) =>
            new() { Kind = pressed ? ControlKind.PttPressed : ControlKind.PttReleased };

        public static ControlInput FloorEvent(string net, FloorInput input, string? holder) =>
            new() { Kind = ControlKind.Floor, NetId = net, Floor = input, Holder = holder };
    }
}
