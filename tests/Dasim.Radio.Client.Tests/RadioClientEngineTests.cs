using Dasim.Radio.Audio;
using Dasim.Radio.Client;
using Dasim.Radio.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dasim.Radio.Client.Tests;

public sealed class RadioClientEngineTests
{
    private const string Net = "alpha";
    private const string Me = "p1";
    private const string ClientId = "c1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        RadioClientEngine Engine,
        FakeAudioBus Bus,
        FakeFloorSignal Floor,
        FakeAudioCaptureDevice Capture,
        FakeAudioPlaybackDevice Playback,
        ManualPushToTalk Ptt);

    private static Harness Build()
    {
        var bus = new FakeAudioBus();
        var floor = new FakeFloorSignal();
        var capture = new FakeAudioCaptureDevice();
        var playback = new FakeAudioPlaybackDevice();
        var ptt = new ManualPushToTalk();
        var options = Options.Create(new ClientOptions { ClientId = ClientId, ParticipantId = Me, OwnNetId = Net });

        var engine = new RadioClientEngine(
            bus, floor, new FakeOpusEncoderFactory(), new FakeOpusDecoderFactory(),
            capture, playback, ptt, options, NullLogger<RadioClientEngine>.Instance);

        return new Harness(engine, bus, floor, capture, playback, ptt);
    }

    private static short[] Pcm(short value)
    {
        short[] frame = new short[AudioFormat.Voice.SamplesPerFrame];
        Array.Fill(frame, value);
        return frame;
    }

    private static FloorEventMessage GrantedToUs() => new(Net, FloorOutcomes.Granted, Me, null, Me);

    private static FloorEventMessage HeldByOther(string who) => new(Net, FloorOutcomes.GrantedWithPreemption, who, Me, who);

    private static async Task WaitForStateAsync(RadioClientEngine engine, Func<ClientRadioState, bool> predicate)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? _, ClientRadioState state)
        {
            if (predicate(state))
            {
                tcs.TrySetResult();
            }
        }

        engine.StateChanged += Handler;
        try
        {
            if (predicate(engine.State))
            {
                return;
            }

            await tcs.Task.WaitAsync(Ct);
        }
        finally
        {
            engine.StateChanged -= Handler;
        }
    }

    [Fact]
    public async Task Received_frames_are_decoded_and_played()
    {
        Harness h = Build();
        await h.Engine.StartAsync(Ct);
        try
        {
            h.Bus.PushMixed([7]);
            await h.Playback.SubmitSignal.Next.WaitAsync(Ct);

            short[] played = Assert.Single(h.Playback.Submitted);
            Assert.Equal(AudioFormat.Voice.SamplesPerFrame, played.Length);
            Assert.Equal((short)7, played[0]); // fake decoder fills PCM from the packet's first byte
        }
        finally
        {
            await h.Engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task An_empty_mixed_frame_is_concealed()
    {
        Harness h = Build();
        await h.Engine.StartAsync(Ct);
        try
        {
            h.Bus.PushMixed([]);
            await h.Playback.SubmitSignal.Next.WaitAsync(Ct);

            short[] played = Assert.Single(h.Playback.Submitted);
            Assert.Equal(AudioFormat.Voice.SamplesPerFrame, played.Length);
            Assert.Equal((short)0, played[0]); // DecodeLost produces silence
        }
        finally
        {
            await h.Engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task Pressing_ptt_requests_the_floor()
    {
        Harness h = Build();
        await h.Engine.StartAsync(Ct);
        try
        {
            h.Ptt.Press();
            await h.Floor.RequestSignal.Next.WaitAsync(Ct);

            FloorRequestMessage request = Assert.Single(h.Floor.Requests);
            Assert.Equal(Net, request.NetId);
            Assert.Equal(Me, request.ParticipantId);
        }
        finally
        {
            await h.Engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task Audio_is_transmitted_only_after_the_floor_is_granted()
    {
        Harness h = Build();
        await h.Engine.StartAsync(Ct);
        try
        {
            h.Ptt.Press();
            await h.Floor.RequestSignal.Next.WaitAsync(Ct);

            // Requesting (not yet granted): a captured frame must NOT be transmitted.
            h.Capture.Capture(Pcm(1));
            Assert.Empty(h.Bus.PublishedCaptured);

            h.Floor.PushEvent(Net, GrantedToUs());
            await WaitForStateAsync(h.Engine, s => s.IsTransmitting);

            h.Capture.Capture(Pcm(2));
            await h.Bus.CapturedSignal.Next.WaitAsync(Ct);

            byte[] sent = Assert.Single(h.Bus.PublishedCaptured);
            Assert.Equal(FakeOpusEncoder.Marker, sent[0]);
        }
        finally
        {
            await h.Engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task Releasing_ptt_releases_the_floor_and_stops_transmitting()
    {
        Harness h = Build();
        await h.Engine.StartAsync(Ct);
        try
        {
            h.Ptt.Press();
            h.Floor.PushEvent(Net, GrantedToUs());
            await WaitForStateAsync(h.Engine, s => s.IsTransmitting);

            h.Ptt.Release();
            await h.Floor.ReleaseSignal.Next.WaitAsync(Ct);
            await WaitForStateAsync(h.Engine, s => s.Phase == PttPhase.Idle);

            FloorReleaseMessage release = Assert.Single(h.Floor.Releases);
            Assert.Equal(Net, release.NetId);

            int before = h.Bus.PublishedCaptured.Count;
            h.Capture.Capture(Pcm(3));
            Assert.Equal(before, h.Bus.PublishedCaptured.Count); // nothing sent after release
        }
        finally
        {
            await h.Engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_preemption_stops_transmission()
    {
        Harness h = Build();
        await h.Engine.StartAsync(Ct);
        try
        {
            h.Ptt.Press();
            h.Floor.PushEvent(Net, GrantedToUs());
            await WaitForStateAsync(h.Engine, s => s.IsTransmitting);

            h.Floor.PushEvent(Net, HeldByOther("bob"));
            await WaitForStateAsync(h.Engine, s => s.Phase == PttPhase.Preempted);

            Assert.False(h.Engine.State.IsTransmitting);
        }
        finally
        {
            await h.Engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task The_floor_holder_is_reflected_in_state()
    {
        Harness h = Build();
        await h.Engine.StartAsync(Ct);
        try
        {
            h.Floor.PushEvent(Net, HeldByOther("bob"));
            await WaitForStateAsync(h.Engine, s => s.FloorHolders.TryGetValue(Net, out string? holder) && holder == "bob");
        }
        finally
        {
            await h.Engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task Stopping_while_holding_the_floor_releases_it()
    {
        Harness h = Build();
        await h.Engine.StartAsync(Ct);

        h.Ptt.Press();
        h.Floor.PushEvent(Net, GrantedToUs());
        await WaitForStateAsync(h.Engine, s => s.IsTransmitting);

        await h.Engine.StopAsync(Ct);

        Assert.Contains(h.Floor.Releases, r => r.NetId == Net && r.ParticipantId == Me);
    }

    [Fact]
    public async Task Stopping_is_idempotent()
    {
        Harness h = Build();
        await h.Engine.StartAsync(Ct);

        await h.Engine.StopAsync(Ct);
        await h.Engine.StopAsync(Ct); // second stop must be a clean no-op
        await h.Engine.DisposeAsync(); // and so must dispose
    }

    [Fact]
    public async Task The_engine_cannot_be_restarted()
    {
        Harness h = Build();
        await h.Engine.StartAsync(Ct);
        await h.Engine.StopAsync(Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Engine.StartAsync(Ct));
    }

    [Fact]
    public async Task A_failing_floor_request_does_not_crash_the_engine()
    {
        Harness h = Build();
        h.Floor.RequestException = new InvalidOperationException("nats down");
        await h.Engine.StartAsync(Ct);
        try
        {
            h.Ptt.Press();
            // The request throws inside the control loop; it must be swallowed and the phase still advances.
            await WaitForStateAsync(h.Engine, s => s.Phase == PttPhase.Requesting);
        }
        finally
        {
            await h.Engine.DisposeAsync();
        }
    }
}
