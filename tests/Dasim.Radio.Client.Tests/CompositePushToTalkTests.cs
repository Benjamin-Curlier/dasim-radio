using Dasim.Radio.Client;
using Xunit;

namespace Dasim.Radio.Client.Tests;

public sealed class CompositePushToTalkTests
{
    private sealed class RecordingSource : IPushToTalkHotkey
    {
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public bool Disposed { get; private set; }

        public event Action? Pressed;
        public event Action? Released;

        public void Press() => Pressed?.Invoke();
        public void Release() => Released?.Invoke();
        public void Start() => Starts++;
        public void Stop() => Stops++;
        public void Dispose() => Disposed = true;
    }

    private static (CompositePushToTalk Composite, ManualPushToTalk A, ManualPushToTalk B, int[] Counts) Build()
    {
        var a = new ManualPushToTalk();
        var b = new ManualPushToTalk();
        var composite = new CompositePushToTalk(a, b);
        int[] counts = [0, 0]; // [pressed, released]
        composite.Pressed += () => counts[0]++;
        composite.Released += () => counts[1]++;
        return (composite, a, b, counts);
    }

    [Fact]
    public void First_source_down_transmits_once()
    {
        (_, ManualPushToTalk a, _, int[] counts) = Build();

        a.Press();

        Assert.Equal(1, counts[0]);
        Assert.Equal(0, counts[1]);
    }

    [Fact]
    public void Overlapping_holds_do_not_re_key_the_floor()
    {
        (_, ManualPushToTalk a, ManualPushToTalk b, int[] counts) = Build();

        a.Press();
        b.Press(); // second source down while first held

        Assert.Equal(1, counts[0]); // still only one Pressed
    }

    [Fact]
    public void Release_only_fires_when_the_last_source_comes_up()
    {
        (_, ManualPushToTalk a, ManualPushToTalk b, int[] counts) = Build();
        a.Press();
        b.Press();

        a.Release();
        Assert.Equal(0, counts[1]); // b still held

        b.Release();
        Assert.Equal(1, counts[1]); // now released
    }

    [Fact]
    public void A_single_source_press_release_cycles_once()
    {
        (_, ManualPushToTalk a, _, int[] counts) = Build();

        a.Press();
        a.Release();

        Assert.Equal(1, counts[0]);
        Assert.Equal(1, counts[1]);
    }

    [Fact]
    public void An_unmatched_release_is_ignored()
    {
        (_, ManualPushToTalk a, _, int[] counts) = Build();

        a.Release(); // never pressed

        Assert.Equal(0, counts[1]);
    }

    [Fact]
    public void Start_stop_and_dispose_fan_out_to_sources()
    {
        var source = new RecordingSource();
        var composite = new CompositePushToTalk(source);

        composite.Start();
        composite.Stop();
        composite.Dispose();

        Assert.Equal(1, source.Starts);
        Assert.Equal(1, source.Stops);
        Assert.True(source.Disposed);
    }

    [Fact]
    public void After_dispose_source_events_are_unsubscribed()
    {
        var source = new RecordingSource();
        int pressed = 0;
        var composite = new CompositePushToTalk(source);
        composite.Pressed += () => pressed++;

        composite.Dispose();
        source.Press(); // should no longer reach the composite

        Assert.Equal(0, pressed);
    }

    [Fact]
    public void At_least_one_source_is_required() =>
        Assert.Throws<ArgumentException>(() => new CompositePushToTalk());
}
