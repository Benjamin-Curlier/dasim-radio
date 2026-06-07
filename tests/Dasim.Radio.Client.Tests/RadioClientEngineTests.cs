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

    // A signal/state we expect within this budget; bounded so a missed transition fails the test fast
    // instead of hanging the run (the engine's pumps are async and run on background tasks).
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(15);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        RadioClientEngine Engine,
        FakeAudioBus Bus,
        FakeFloorSignal Floor,
        FakeAudioCaptureDevice Capture,
        FakeAudioPlaybackDevice Playback,
        ManualPushToTalk Ptt);

    private static Harness Build(TimeSpan? floorReadyTimeout = null)
    {
        var bus = new FakeAudioBus();
        var floor = new FakeFloorSignal();
        var capture = new FakeAudioCaptureDevice();
        var playback = new FakeAudioPlaybackDevice();
        var ptt = new ManualPushToTalk();
        var options = Options.Create(new ClientOptions
        {
            ClientId = ClientId,
            ParticipantId = Me,
            OwnNetId = Net,
            FloorSubscribeReadyTimeout = floorReadyTimeout ?? TimeSpan.FromSeconds(5),
        });

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
            // Subscribe first, then check the current state — so a transition that already happened
            // (or happens before we await) is never missed.
            if (predicate(engine.State))
            {
                return;
            }

            await tcs.Task.WaitAsync(WaitBudget, Ct);
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
            // Capture the signal BEFORE triggering: the receive pump may fire before we'd otherwise read
            // .Next (which would hand back a fresh, never-completing task).
            Task submitted = h.Playback.SubmitSignal.Next;
            h.Bus.PushMixed([7]);
            await submitted.WaitAsync(WaitBudget, Ct);

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
            Task submitted = h.Playback.SubmitSignal.Next;
            h.Bus.PushMixed([]);
            await submitted.WaitAsync(WaitBudget, Ct);

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
            Task requested = h.Floor.RequestSignal.Next;
            h.Ptt.Press();
            await requested.WaitAsync(WaitBudget, Ct);

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
            Task requested = h.Floor.RequestSignal.Next;
            h.Ptt.Press();
            await requested.WaitAsync(WaitBudget, Ct);

            // Requesting (not yet granted): a captured frame must NOT be transmitted.
            h.Capture.Capture(Pcm(1));
            Assert.Empty(h.Bus.PublishedCaptured);

            h.Floor.PushEvent(Net, GrantedToUs());
            await WaitForStateAsync(h.Engine, s => s.IsTransmitting);

            Task captured = h.Bus.CapturedSignal.Next;
            h.Capture.Capture(Pcm(2));
            await captured.WaitAsync(WaitBudget, Ct);

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

            Task released = h.Floor.ReleaseSignal.Next;
            h.Ptt.Release();
            await released.WaitAsync(WaitBudget, Ct);
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
    public async Task Start_waits_for_floor_subscriptions_before_enabling_input()
    {
        Harness h = Build();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Floor.SubscribeGate = gate.Task;

        Task start = h.Engine.StartAsync(Ct);

        // While the floor-event subscriptions haven't reported ready, StartAsync must not complete — so
        // PTT input (enabled only after that await) can't request a floor whose grant we couldn't hear.
        await Task.Yield();
        Assert.False(start.IsCompleted);

        gate.SetResult(); // the subscriptions become live on the server
        await start.WaitAsync(WaitBudget, Ct);

        Assert.True(start.IsCompletedSuccessfully);
        await h.Engine.DisposeAsync();
    }

    [Fact]
    public async Task Start_enables_input_after_the_timeout_when_subscriptions_never_report_ready()
    {
        Harness h = Build(floorReadyTimeout: TimeSpan.FromMilliseconds(50));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Floor.SubscribeGate = gate.Task; // never released — the subscriptions stay un-ready

        // Despite the subscriptions never confirming, StartAsync gives up after the (tiny) timeout and
        // enables input rather than hanging — best-effort startup on a slow/absent broker.
        await h.Engine.StartAsync(Ct);

        try
        {
            Task requested = h.Floor.RequestSignal.Next;
            h.Ptt.Press();
            await requested.WaitAsync(WaitBudget, Ct);
            Assert.Single(h.Floor.Requests);
        }
        finally
        {
            gate.SetResult();
            await h.Engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task Stop_racing_the_startup_readiness_wait_does_not_leave_input_enabled()
    {
        Harness h = Build();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Floor.SubscribeGate = gate.Task; // hold the subscriptions un-ready so Start parks in its await

        Task start = h.Engine.StartAsync(Ct);
        await Task.Yield();
        Assert.False(start.IsCompleted);

        // Stop races in while Start is still waiting for the subscriptions to report ready.
        await h.Engine.StopAsync(Ct);

        // Start then unwinds — either bails on the post-await lifecycle re-check, or observes the stop's
        // cancellation. Both are clean; what must NOT happen is input being wired onto the stopped engine.
        try
        {
            await start.WaitAsync(WaitBudget, Ct);
        }
        catch (OperationCanceledException)
        {
            // Acceptable: the readiness wait observed the stop's token cancellation.
        }

        h.Ptt.Press();
        Assert.False(h.Capture.Started); // capture was never started onto the stopped engine
        Assert.Empty(h.Floor.Requests); // and PTT input reaches no handler
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
