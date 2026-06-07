using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Dasim.Radio.Audio;
using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.Audio;
using Dasim.Radio.Messaging.Floor;

namespace Dasim.Radio.Client.Tests;

/// <summary>One-shot async signal so a test can await the next observable side effect deterministically.</summary>
internal sealed class AsyncSignal
{
    private readonly Lock _gate = new();
    private TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Next
    {
        get
        {
            lock (_gate)
            {
                return _tcs.Task;
            }
        }
    }

    public void Fire()
    {
        TaskCompletionSource toComplete;
        lock (_gate)
        {
            toComplete = _tcs;
            _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        toComplete.TrySetResult();
    }
}

/// <summary>An <see cref="IAudioBus"/> that records captured publishes and replays a pushable mixed stream.</summary>
internal sealed class FakeAudioBus : IAudioBus
{
    private readonly Channel<byte[]> _mixed = Channel.CreateUnbounded<byte[]>();

    public List<byte[]> PublishedCaptured { get; } = [];

    public AsyncSignal CapturedSignal { get; } = new();

    /// <summary>Pushes one frame onto this client's mix stream.</summary>
    public void PushMixed(byte[] frame) => _mixed.Writer.TryWrite(frame);

    public ValueTask PublishCapturedAsync(string clientId, ReadOnlyMemory<byte> opusFrame, CancellationToken cancellationToken = default)
    {
        PublishedCaptured.Add(opusFrame.ToArray());
        CapturedSignal.Fire();
        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerable<byte[]> SubscribeMixedAsync(string clientId, CancellationToken cancellationToken = default) =>
        _mixed.Reader.ReadAllAsync(cancellationToken);

    public ValueTask PublishMixedAsync(string listenerClientId, ReadOnlyMemory<byte> opusFrame, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<AudioFrame> SubscribeCapturedAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>An <see cref="IFloorSignal"/> that records requests/releases and replays pushable per-net events.</summary>
internal sealed class FakeFloorSignal : IFloorSignal
{
    private readonly Dictionary<string, Channel<FloorEventMessage>> _events = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public List<FloorRequestMessage> Requests { get; } = [];

    public List<FloorReleaseMessage> Releases { get; } = [];

    public AsyncSignal RequestSignal { get; } = new();

    public AsyncSignal ReleaseSignal { get; } = new();

    /// <summary>When set, <see cref="RequestAsync"/> throws this (to exercise the engine's resilience).</summary>
    public Exception? RequestException { get; set; }

    /// <summary>When set, a subscription awaits this before reporting itself ready — exercises the startup gate.</summary>
    public Task? SubscribeGate { get; set; }

    /// <summary>Pushes a floor event onto a net's stream.</summary>
    public void PushEvent(string net, FloorEventMessage @event) => EventChannel(net).Writer.TryWrite(@event);

    public ValueTask RequestAsync(FloorRequestMessage request, CancellationToken cancellationToken = default)
    {
        if (RequestException is not null)
        {
            throw RequestException;
        }

        Requests.Add(request);
        RequestSignal.Fire();
        return ValueTask.CompletedTask;
    }

    public ValueTask ReleaseAsync(FloorReleaseMessage release, CancellationToken cancellationToken = default)
    {
        Releases.Add(release);
        ReleaseSignal.Fire();
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<FloorEventMessage> SubscribeEventsAsync(
        string netId, Action? onSubscribed = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (SubscribeGate is { } gate)
        {
            // Token-aware so a shutdown during the gated wait unblocks the subscription (mirrors a real
            // SUB being cancelled) instead of hanging teardown.
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        onSubscribed?.Invoke();

        await foreach (FloorEventMessage @event in EventChannel(netId).Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return @event;
        }
    }

    public ValueTask PublishEventAsync(FloorEventMessage @event, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<FloorRequestMessage> SubscribeRequestsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<FloorReleaseMessage> SubscribeReleasesAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    private Channel<FloorEventMessage> EventChannel(string net)
    {
        lock (_gate)
        {
            if (!_events.TryGetValue(net, out Channel<FloorEventMessage>? channel))
            {
                channel = Channel.CreateUnbounded<FloorEventMessage>();
                _events[net] = channel;
            }

            return channel;
        }
    }
}

/// <summary>An <see cref="IAudioCaptureDevice"/> whose frames a test raises explicitly.</summary>
internal sealed class FakeAudioCaptureDevice : IAudioCaptureDevice
{
    public AudioFormat Format => AudioFormat.Voice;

    public bool Started { get; private set; }

    public event AudioFrameHandler? FrameCaptured;

    /// <summary>Raises one captured frame (as the audio thread would).</summary>
    public void Capture(ReadOnlySpan<short> frame) => FrameCaptured?.Invoke(frame);

    public void Start() => Started = true;

    public void Stop() => Started = false;

    public void Dispose()
    {
    }
}

/// <summary>An <see cref="IAudioPlaybackDevice"/> that records submitted PCM.</summary>
internal sealed class FakeAudioPlaybackDevice : IAudioPlaybackDevice
{
    public AudioFormat Format => AudioFormat.Voice;

    public bool Started { get; private set; }

    public List<short[]> Submitted { get; } = [];

    public AsyncSignal SubmitSignal { get; } = new();

    public void Submit(ReadOnlySpan<short> pcm)
    {
        Submitted.Add(pcm.ToArray());
        SubmitSignal.Fire();
    }

    public void Start() => Started = true;

    public void Stop() => Started = false;

    public void Dispose()
    {
    }
}

/// <summary>A fake encoder that emits a 2-byte marker frame so a published transmit frame is observable.</summary>
internal sealed class FakeOpusEncoder : IOpusEncoder
{
    public const byte Marker = 0xEE;

    public AudioFormat Format => AudioFormat.Voice;

    public int Encode(ReadOnlySpan<short> pcm, Span<byte> output)
    {
        output[0] = Marker;
        output[1] = (byte)(pcm.Length > 0 ? pcm[0] & 0xFF : 0);
        return 2;
    }

    public void Retune(OpusEncoderSettings settings)
    {
    }

    public void Dispose()
    {
    }
}

/// <summary>A fake decoder that fills PCM from the packet's first byte (or zero for loss concealment).</summary>
internal sealed class FakeOpusDecoder : IOpusDecoder
{
    public AudioFormat Format => AudioFormat.Voice;

    public int Decode(ReadOnlySpan<byte> opus, Span<short> pcm)
    {
        pcm[..AudioFormat.Voice.SamplesPerFrame].Fill(opus.Length > 0 ? opus[0] : (short)0);
        return AudioFormat.Voice.SamplesPerChannel;
    }

    public int DecodeFec(ReadOnlySpan<byte> nextPacket, Span<short> pcm)
    {
        pcm[..AudioFormat.Voice.SamplesPerFrame].Clear();
        return AudioFormat.Voice.SamplesPerChannel;
    }

    public int DecodeLost(Span<short> pcm)
    {
        pcm[..AudioFormat.Voice.SamplesPerFrame].Clear();
        return AudioFormat.Voice.SamplesPerChannel;
    }

    public void Dispose()
    {
    }
}

internal sealed class FakeOpusEncoderFactory : IOpusEncoderFactory
{
    public IOpusEncoder Create(AudioFormat format, OpusEncoderSettings? settings = null) => new FakeOpusEncoder();
}

internal sealed class FakeOpusDecoderFactory : IOpusDecoderFactory
{
    public IOpusDecoder Create(AudioFormat format) => new FakeOpusDecoder();
}
