using Dasim.Radio.Audio;
using Dasim.Radio.Audio.Opus;
using Xunit;

namespace Dasim.Radio.Audio.Opus.Tests;

// These run on both windows-latest and ubuntu-latest in CI, so a green run proves
// OpusSharp.Natives' native libopus (win-x64 opus.dll, linux-x64 opus.so) loads and P/Invokes
// correctly on both deployment RIDs.
public sealed class OpusSharpCodecTests
{
    private static readonly IOpusEncoderFactory EncoderFactory = new OpusSharpEncoderFactory();
    private static readonly IOpusDecoderFactory DecoderFactory = new OpusSharpDecoderFactory();

    [Fact]
    public void Encode_then_decode_round_trips_real_audio()
    {
        AudioFormat fmt = AudioFormat.Voice;
        using IOpusEncoder encoder = EncoderFactory.Create(fmt);
        using IOpusDecoder decoder = DecoderFactory.Create(fmt);

        byte[] packet = new byte[OpusConstants.RecommendedMaxPacketBytes];
        short[] decoded = new short[fmt.SamplesPerFrame];
        double peakRms = 0;

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

        int recovered = decoder.DecodeFec(packet1.AsSpan(0, bytes1), pcm);
        Assert.Equal(fmt.SamplesPerChannel, recovered);

        int current = decoder.Decode(packet1.AsSpan(0, bytes1), pcm);
        Assert.Equal(fmt.SamplesPerChannel, current);
    }

    [Fact]
    public void Dtx_emits_tiny_frames_during_silence()
    {
        AudioFormat fmt = AudioFormat.Voice;
        using IOpusEncoder encoder = EncoderFactory.Create(fmt, new OpusEncoderSettings { UseDtx = true });

        byte[] packet = new byte[OpusConstants.RecommendedMaxPacketBytes];
        short[] silence = new short[fmt.SamplesPerFrame];
        int smallest = int.MaxValue;

        // libopus's SILK VAD needs sustained silence (hundreds of ms) before DTX engages and it
        // starts returning ≤2-byte "do not transmit" frames, so feed ~1 s of digital silence.
        for (int frame = 0; frame < 50; frame++)
        {
            int bytes = encoder.Encode(silence, packet);
            smallest = Math.Min(smallest, bytes);
        }

        Assert.True(smallest <= 2, $"expected a DTX (≤2-byte) frame, smallest was {smallest} bytes");
    }

    [Fact]
    public void Encode_does_not_mutate_the_input_pcm()
    {
        // Locks the precondition behind OpusSharpInterop.AsNativeConstInput: libopus must treat the
        // PCM as const, so reinterpreting the caller's ReadOnlySpan as writable cannot corrupt it.
        AudioFormat fmt = AudioFormat.Voice;
        using IOpusEncoder encoder = EncoderFactory.Create(fmt);

        short[] pcm = Sine(fmt, 440, 0);
        short[] original = (short[])pcm.Clone();
        byte[] packet = new byte[OpusConstants.RecommendedMaxPacketBytes];

        encoder.Encode(pcm, packet);

        Assert.Equal(original, pcm);
    }

    [Fact]
    public void Encode_rejects_wrong_frame_length()
    {
        using IOpusEncoder encoder = EncoderFactory.Create(AudioFormat.Voice);
        byte[] packet = new byte[OpusConstants.RecommendedMaxPacketBytes];

        Assert.Throws<ArgumentException>(() => encoder.Encode(new short[100], packet));
    }

    [Fact]
    public void Decode_rejects_an_empty_packet()
    {
        using IOpusDecoder decoder = DecoderFactory.Create(AudioFormat.Voice);
        short[] pcm = new short[AudioFormat.Voice.SamplesPerFrame];

        Assert.Throws<ArgumentException>(() => decoder.Decode(ReadOnlySpan<byte>.Empty, pcm));
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
