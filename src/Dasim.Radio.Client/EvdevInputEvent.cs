using System.Buffers.Binary;

namespace Dasim.Radio.Client;

/// <summary>
/// One Linux <c>input_event</c> record (the <c>type</c>/<c>code</c>/<c>value</c> triple; the leading
/// timestamp is ignored). Used by the evdev push-to-talk provider, which reads <c>/dev/input/event*</c>.
/// </summary>
public readonly record struct EvdevInputEvent(ushort Type, ushort Code, int Value);

/// <summary>Parses raw <c>input_event</c> records read from <c>/dev/input/event*</c>.</summary>
public static class EvdevInputEventParser
{
    /// <summary>
    /// Size in bytes of one <c>input_event</c> on a 64-bit kernel: a 16-byte <c>timeval</c> (two 8-byte
    /// longs) followed by <c>__u16 type</c>, <c>__u16 code</c>, <c>__s32 value</c>.
    /// </summary>
    public const int RecordSize = 24;

    /// <summary><c>EV_KEY</c> — a key/button state change.</summary>
    public const ushort EventTypeKey = 0x01;

    /// <summary><c>value</c> for a key release.</summary>
    public const int KeyReleased = 0;

    /// <summary><c>value</c> for a key press.</summary>
    public const int KeyPressed = 1;

    /// <summary><c>value</c> for an auto-repeat while a key is held.</summary>
    public const int KeyRepeat = 2;

    /// <summary>
    /// Parses one record from the front of <paramref name="record"/> (little-endian, the layout of
    /// x86-64 / arm64). Returns <c>false</c> if the span is shorter than <see cref="RecordSize"/>.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> record, out EvdevInputEvent input)
    {
        if (record.Length < RecordSize)
        {
            input = default;
            return false;
        }

        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(record[16..18]);
        ushort code = BinaryPrimitives.ReadUInt16LittleEndian(record[18..20]);
        int value = BinaryPrimitives.ReadInt32LittleEndian(record[20..24]);
        input = new EvdevInputEvent(type, code, value);
        return true;
    }
}
