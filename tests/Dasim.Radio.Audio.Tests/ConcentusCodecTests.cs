using Dasim.Radio.Audio;
using Dasim.Radio.Audio.Concentus;
using Xunit;

namespace Dasim.Radio.Audio.Tests;

public sealed class ConcentusCodecTests
{
    private static readonly IOpusEncoderFactory EncoderFactory = new ConcentusOpusEncoderFactory();
    private static readonly IOpusDecoderFactory DecoderFactory = new ConcentusOpusDecoderFactory();

    [Fact]
    public void Encode_then_decode_round_trips_real_audio()
    {
        AudioFormat fmt = AudioFormat.Voice;
        using IOpusEncoder encoder = EncoderFactory.Create(fmt);
        using IOpusDecoder decoder = DecoderFactory.Create(fmt);

        byte[] packet = new byte[OpusConstants.RecommendedMaxPacketBytes];
        short[] decoded = new short[fmt.SamplesPerFrame];
        double peakRms = 0;

        // Push several frames so the decode clears the encoder's lookahead/warm-up.
        for (int frame = 0; frame < 15; frame++)
        {
            int bytes = encoder.Encode(Sine(fmt, 440, frame), packet);
            Assert.True(bytes > 2, $"frame {frame} produced only {bytes} bytes");

            int samples = decoder.Decode(packet.AsSpan(0, bytes), decoded);
            Assert.Equal(fmt.SamplesPerChannel, samples);

            peakRms = Math.Max(peakRms, Rms(decoded));
        }

        Assert.True(peakRms > 1_000, $"decoded signal energy too low (RMS {peakRms:F0}) — audio did not round-trip");
    }

    [Fact]
    public void DecodeLost_conceals_a_dropped_packet()
    {
        AudioFormat fmt = AudioFormat.Voice;
        using IOpusEncoder encoder = EncoderFactory.Create(fmt);
        using IOpusDecoder decoder = DecoderFactory.Create(fmt);

        byte[] packet = new byte[OpusConstants.RecommendedMaxPacketBytes];
        short[] decoded = new short[fmt.SamplesPerFrame];

        // Prime the decoder so packet-loss concealment has recent history to work from.
        for (int frame = 0; frame < 3; frame++)
        {
            int bytes = encoder.Encode(Sine(fmt, 440, frame), packet);
            decoder.Decode(packet.AsSpan(0, bytes), decoded);
        }

        int concealed = decoder.DecodeLost(decoded);

        Assert.Equal(fmt.SamplesPerChannel, concealed);
    }

    [Fact]
    public void DecodeFec_recovers_a_lost_frame_from_the_following_packet()
    {
        AudioFormat fmt = AudioFormat.Voice;
        var settings = new OpusEncoderSettings { UseInBandFec = true, ExpectedPacketLossPercent = 30 };
        using IOpusEncoder encoder = EncoderFactory.Create(fmt, settings);
        using IOpusDecoder decoder = DecoderFactory.Create(fmt);

        byte[] packet0 = new byte[OpusConstants.RecommendedMaxPacketBytes];
        byte[] packet1 = new byte[OpusConstants.RecommendedMaxPacketBytes];
        short[] pcm = new short[fmt.SamplesPerFrame];

        int bytes0 = encoder.Encode(Sine(fmt, 440, 0), packet0);
        int bytes1 = encoder.Encode(Sine(fmt, 440, 1), packet1);
        Assert.True(bytes0 > 0 && bytes1 > 0);

        // Frame 0 is "lost": reconstruct it from packet 1's FEC, then decode packet 1 in order.
        int recovered = decoder.DecodeFec(packet1.AsSpan(0, bytes1), pcm);
        Assert.Equal(fmt.SamplesPerChannel, recovered);

        int current = decoder.Decode(packet1.AsSpan(0, bytes1), pcm);
        Assert.Equal(fmt.SamplesPerChannel, current);
    }

    [Fact]
    public void Decode_rejects_an_empty_packet()
    {
        using IOpusDecoder decoder = DecoderFactory.Create(AudioFormat.Voice);
        short[] pcm = new short[AudioFormat.Voice.SamplesPerFrame];

        Assert.Throws<ArgumentException>(() => decoder.Decode(ReadOnlySpan<byte>.Empty, pcm));
    }

    [Fact]
    public void Dtx_emits_tiny_frames_during_silence()
    {
        AudioFormat fmt = AudioFormat.Voice;
        using IOpusEncoder encoder = EncoderFactory.Create(fmt, new OpusEncoderSettings { UseDtx = true });

        byte[] packet = new byte[OpusConstants.RecommendedMaxPacketBytes];
        short[] silence = new short[fmt.SamplesPerFrame];
        int smallest = int.MaxValue;

        // DTX kicks in after a few silent frames; one of them should be a 0/1-byte frame.
        for (int frame = 0; frame < 10; frame++)
        {
            int bytes = encoder.Encode(silence, packet);
            smallest = Math.Min(smallest, bytes);
        }

        Assert.True(smallest <= 2, $"expected a DTX (≤2-byte) frame, smallest was {smallest} bytes");
    }

    [Fact]
    public void Encode_rejects_wrong_frame_length()
    {
        using IOpusEncoder encoder = EncoderFactory.Create(AudioFormat.Voice);
        byte[] packet = new byte[OpusConstants.RecommendedMaxPacketBytes];

        Assert.Throws<ArgumentException>(() => encoder.Encode(new short[100], packet));
    }

    [Fact]
    public void Decode_rejects_too_small_pcm_buffer()
    {
        AudioFormat fmt = AudioFormat.Voice;
        using IOpusEncoder encoder = EncoderFactory.Create(fmt);
        using IOpusDecoder decoder = DecoderFactory.Create(fmt);

        byte[] packet = new byte[OpusConstants.RecommendedMaxPacketBytes];
        int bytes = encoder.Encode(Sine(fmt, 440, 0), packet);

        Assert.Throws<ArgumentException>(() => decoder.Decode(packet.AsSpan(0, bytes), new short[100]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void Factory_rejects_out_of_range_complexity(int complexity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EncoderFactory.Create(AudioFormat.Voice, new OpusEncoderSettings { Complexity = complexity }));
    }

    [Fact]
    public void Factory_rejects_non_positive_bitrate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EncoderFactory.Create(AudioFormat.Voice, new OpusEncoderSettings { BitrateBitsPerSecond = 0 }));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Factory_rejects_out_of_range_packet_loss(int loss)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EncoderFactory.Create(AudioFormat.Voice, new OpusEncoderSettings { ExpectedPacketLossPercent = loss }));
    }

    private static short[] Sine(AudioFormat format, double frequencyHz, int frameIndex)
    {
        short[] pcm = new short[format.SamplesPerFrame];
        int perChannel = format.SamplesPerChannel;
        long start = (long)frameIndex * perChannel;

        for (int i = 0; i < perChannel; i++)
        {
            double t = (start + i) / (double)format.SampleRateHz;
            short sample = (short)(Math.Sin(2 * Math.PI * frequencyHz * t) * 0.3 * short.MaxValue);

            for (int c = 0; c < format.Channels; c++)
            {
                pcm[(i * format.Channels) + c] = sample;
            }
        }

        return pcm;
    }

    private static double Rms(ReadOnlySpan<short> pcm)
    {
        double sumOfSquares = 0;
        foreach (short sample in pcm)
        {
            sumOfSquares += (double)sample * sample;
        }

        return Math.Sqrt(sumOfSquares / pcm.Length);
    }
}
