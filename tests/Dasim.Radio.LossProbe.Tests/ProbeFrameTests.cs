using Dasim.Radio.LossProbe;
using Xunit;

namespace Dasim.Radio.LossProbe.Tests;

public sealed class ProbeFrameTests
{
    [Theory]
    [InlineData(0u, 0L)]
    [InlineData(1u, 123_456_789L)]
    [InlineData(uint.MaxValue, long.MaxValue)]
    public void Header_round_trips(uint sequence, long timestamp)
    {
        byte[] frame = new byte[ProbeFrame.HeaderBytes + 8];
        ProbeFrame.WriteHeader(frame, sequence, timestamp);

        Assert.True(ProbeFrame.TryReadHeader(frame, out uint readSeq, out long readTs));
        Assert.Equal(sequence, readSeq);
        Assert.Equal(timestamp, readTs);
    }

    [Fact]
    public void Writing_into_a_buffer_leaves_the_filler_region_untouched()
    {
        byte[] frame = new byte[ProbeFrame.HeaderBytes + 4];
        frame.AsSpan(ProbeFrame.HeaderBytes).Fill(ProbeFrame.FillerByte);

        ProbeFrame.WriteHeader(frame, sequence: 7, sendTimestampTicks: 99);

        for (int i = ProbeFrame.HeaderBytes; i < frame.Length; i++)
        {
            Assert.Equal(ProbeFrame.FillerByte, frame[i]);
        }
    }

    [Fact]
    public void A_too_short_payload_is_rejected()
    {
        byte[] tooShort = new byte[ProbeFrame.HeaderBytes - 1];

        Assert.False(ProbeFrame.TryReadHeader(tooShort, out uint seq, out long ts));
        Assert.Equal(0u, seq);
        Assert.Equal(0L, ts);
    }

    [Fact]
    public void Writing_into_a_too_small_buffer_throws()
    {
        byte[] tooSmall = new byte[ProbeFrame.HeaderBytes - 1];

        Assert.Throws<ArgumentException>(() => ProbeFrame.WriteHeader(tooSmall, 1, 1));
    }
}
