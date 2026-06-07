using System.Buffers.Binary;

namespace Dasim.Radio.LossProbe;

/// <summary>
/// The probe's wire layout for one synthetic voice frame: a fixed header (monotonic sequence number +
/// the sender's <see cref="System.Diagnostics.Stopwatch"/> timestamp) followed by filler padding the
/// frame to the requested size. This mirrors the only thing a real <c>audio.out</c> frame would need to
/// carry to drive loss detection — a sequence number — so the measurement is representative of the
/// scheme under evaluation. The header is written in place into a reused buffer, so the publisher's hot
/// loop allocates nothing per frame, exactly like the production transmit pump.
/// </summary>
public static class ProbeFrame
{
    /// <summary>Header size in bytes: 4 (UInt32 sequence) + 8 (Int64 send timestamp ticks).</summary>
    public const int HeaderBytes = 12;

    /// <summary>The byte the filler region is set to (a recognisable, non-zero pattern).</summary>
    public const byte FillerByte = 0xAB;

    /// <summary>Writes the header into the start of <paramref name="frame"/> (big-endian).</summary>
    public static void WriteHeader(Span<byte> frame, uint sequence, long sendTimestampTicks)
    {
        if (frame.Length < HeaderBytes)
        {
            throw new ArgumentException(
                $"Frame must be at least {HeaderBytes} bytes for the probe header.", nameof(frame));
        }

        BinaryPrimitives.WriteUInt32BigEndian(frame[..4], sequence);
        BinaryPrimitives.WriteInt64BigEndian(frame.Slice(4, 8), sendTimestampTicks);
    }

    /// <summary>
    /// Reads the header from <paramref name="frame"/>. Returns false (and zeroed outputs) when the
    /// payload is too short to be one of ours — so a stray message on the subject is ignored rather than
    /// miscounted.
    /// </summary>
    public static bool TryReadHeader(ReadOnlySpan<byte> frame, out uint sequence, out long sendTimestampTicks)
    {
        if (frame.Length < HeaderBytes)
        {
            sequence = 0;
            sendTimestampTicks = 0;
            return false;
        }

        sequence = BinaryPrimitives.ReadUInt32BigEndian(frame[..4]);
        sendTimestampTicks = BinaryPrimitives.ReadInt64BigEndian(frame.Slice(4, 8));
        return true;
    }
}
