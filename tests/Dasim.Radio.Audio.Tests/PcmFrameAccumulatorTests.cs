using Dasim.Radio.Audio;
using Xunit;

namespace Dasim.Radio.Audio.Tests;

public sealed class PcmFrameAccumulatorTests
{
    private static (PcmFrameAccumulator Accumulator, List<short[]> Frames) Build(int frameSamples = 4)
    {
        var frames = new List<short[]>();
        var accumulator = new PcmFrameAccumulator(frameSamples);
        return (accumulator, frames);
    }

    [Fact]
    public void Emits_a_frame_once_enough_samples_arrive()
    {
        (PcmFrameAccumulator accumulator, List<short[]> frames) = Build(frameSamples: 4);
        AudioFrameHandler onFrame = span => frames.Add(span.ToArray());

        accumulator.Append([0f, 0.5f, -0.5f], onFrame); // 3 samples — not yet a frame
        Assert.Empty(frames);
        Assert.Equal(3, accumulator.Pending);

        accumulator.Append([1f], onFrame); // completes the 4-sample frame
        short[] frame = Assert.Single(frames);
        Assert.Equal([0, 16384, -16384, 32767], frame);
        Assert.Equal(0, accumulator.Pending);
    }

    [Fact]
    public void Emits_multiple_frames_from_one_append()
    {
        (PcmFrameAccumulator accumulator, List<short[]> frames) = Build(frameSamples: 2);
        AudioFrameHandler onFrame = span => frames.Add(span.ToArray());

        accumulator.Append([0f, 0f, 0f, 0f, 0f], onFrame); // 5 samples -> two frames, 1 left over

        Assert.Equal(2, frames.Count);
        Assert.Equal(1, accumulator.Pending);
    }

    [Fact]
    public void A_partial_append_emits_nothing()
    {
        (PcmFrameAccumulator accumulator, List<short[]> frames) = Build(frameSamples: 4);

        accumulator.Append([0f, 0f], span => frames.Add(span.ToArray()));

        Assert.Empty(frames);
        Assert.Equal(2, accumulator.Pending);
    }

    [Fact]
    public void Reset_discards_the_partial_frame()
    {
        (PcmFrameAccumulator accumulator, List<short[]> frames) = Build(frameSamples: 4);
        accumulator.Append([0f, 0f], span => frames.Add(span.ToArray()));

        accumulator.Reset();

        Assert.Equal(0, accumulator.Pending);
    }

    [Fact]
    public void A_non_positive_frame_size_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PcmFrameAccumulator(0));
}
