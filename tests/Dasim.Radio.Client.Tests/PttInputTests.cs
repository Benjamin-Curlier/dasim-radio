using System.Buffers.Binary;
using Dasim.Radio.Client;
using Xunit;

namespace Dasim.Radio.Client.Tests;

public sealed class SessionTypeDetectorTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] pairs)
    {
        var map = pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        return key => map.TryGetValue(key, out string? value) ? value : null;
    }

    [Theory]
    [InlineData("wayland")]
    [InlineData("WAYLAND")]
    public void Xdg_wayland_is_wayland(string value) =>
        Assert.Equal(SessionType.Wayland, SessionTypeDetector.Detect(Env(("XDG_SESSION_TYPE", value))));

    [Theory]
    [InlineData("x11")]
    [InlineData("X11")]
    public void Xdg_x11_is_x11(string value) =>
        Assert.Equal(SessionType.X11, SessionTypeDetector.Detect(Env(("XDG_SESSION_TYPE", value))));

    [Fact]
    public void Wayland_display_without_xdg_is_wayland() =>
        Assert.Equal(SessionType.Wayland, SessionTypeDetector.Detect(Env(("WAYLAND_DISPLAY", "wayland-0"))));

    [Fact]
    public void X11_display_without_xdg_is_x11() =>
        Assert.Equal(SessionType.X11, SessionTypeDetector.Detect(Env(("DISPLAY", ":0"))));

    [Fact]
    public void Nothing_set_is_other() =>
        Assert.Equal(SessionType.Other, SessionTypeDetector.Detect(Env(("XDG_SESSION_TYPE", "tty"))));

    [Fact]
    public void Xdg_session_type_takes_precedence_over_display_sockets()
    {
        // XDG says x11 even though a Wayland socket is also present — XDG must win.
        SessionType result = SessionTypeDetector.Detect(Env(("XDG_SESSION_TYPE", "x11"), ("WAYLAND_DISPLAY", "wayland-0")));

        Assert.Equal(SessionType.X11, result);
    }

    [Fact]
    public void A_null_lookup_is_rejected() =>
        Assert.Throws<ArgumentNullException>(() => SessionTypeDetector.Detect(null!));
}

public sealed class EvdevInputEventParserTests
{
    private static byte[] Record(ushort type, ushort code, int value)
    {
        byte[] buffer = new byte[EvdevInputEventParser.RecordSize];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(16, 2), type);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(18, 2), code);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(20, 4), value);
        return buffer;
    }

    [Fact]
    public void Parses_a_key_press_record()
    {
        byte[] record = Record(EvdevInputEventParser.EventTypeKey, code: 30, EvdevInputEventParser.KeyPressed);

        Assert.True(EvdevInputEventParser.TryParse(record, out EvdevInputEvent input));
        Assert.Equal(EvdevInputEventParser.EventTypeKey, input.Type);
        Assert.Equal((ushort)30, input.Code);
        Assert.Equal(EvdevInputEventParser.KeyPressed, input.Value);
    }

    [Fact]
    public void A_short_buffer_is_rejected()
    {
        Assert.False(EvdevInputEventParser.TryParse(new byte[EvdevInputEventParser.RecordSize - 1], out _));
    }

    [Fact]
    public void Parses_a_golden_kernel_byte_pattern()
    {
        // 16-byte zero timeval, then type=0x0001 (EV_KEY), code=0x001E (30), value=0x00000001, all LE.
        // Hand-built (not via the LE writer helper) so it independently pins the offsets + byte order.
        byte[] record =
        [
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, // timeval
            0x01, 0x00, // type
            0x1E, 0x00, // code
            0x01, 0x00, 0x00, 0x00, // value
        ];

        Assert.True(EvdevInputEventParser.TryParse(record, out EvdevInputEvent input));
        Assert.Equal(EvdevInputEventParser.EventTypeKey, input.Type);
        Assert.Equal((ushort)30, input.Code);
        Assert.Equal(1, input.Value);
    }

    [Fact]
    public void Parses_the_first_record_from_a_multi_record_buffer()
    {
        // /dev/input reads return multiples of RecordSize; TryParse reads from the front and ignores the rest.
        byte[] first = Record(EvdevInputEventParser.EventTypeKey, code: 42, EvdevInputEventParser.KeyPressed);
        byte[] buffer = [.. first, .. Record(EvdevInputEventParser.EventTypeKey, code: 99, EvdevInputEventParser.KeyReleased)];

        Assert.True(EvdevInputEventParser.TryParse(buffer, out EvdevInputEvent input));
        Assert.Equal((ushort)42, input.Code);
        Assert.Equal(EvdevInputEventParser.KeyPressed, input.Value);
    }
}

public sealed class PttKeyStateTests
{
    private const ushort TargetKey = 30; // e.g. KEY_A

    private static EvdevInputEvent KeyEvent(ushort code, int value) =>
        new(EvdevInputEventParser.EventTypeKey, code, value);

    [Fact]
    public void A_press_then_release_yields_one_edge_each()
    {
        var state = new PttKeyState(TargetKey);

        Assert.Equal(PttEdge.Pressed, state.Apply(KeyEvent(TargetKey, EvdevInputEventParser.KeyPressed)));
        Assert.True(state.IsDown);
        Assert.Equal(PttEdge.Released, state.Apply(KeyEvent(TargetKey, EvdevInputEventParser.KeyReleased)));
        Assert.False(state.IsDown);
    }

    [Fact]
    public void Auto_repeat_is_ignored()
    {
        var state = new PttKeyState(TargetKey);
        state.Apply(KeyEvent(TargetKey, EvdevInputEventParser.KeyPressed));

        Assert.Equal(PttEdge.None, state.Apply(KeyEvent(TargetKey, EvdevInputEventParser.KeyRepeat)));
        Assert.True(state.IsDown);
    }

    [Fact]
    public void A_redundant_press_does_not_re_fire()
    {
        var state = new PttKeyState(TargetKey);
        state.Apply(KeyEvent(TargetKey, EvdevInputEventParser.KeyPressed));

        Assert.Equal(PttEdge.None, state.Apply(KeyEvent(TargetKey, EvdevInputEventParser.KeyPressed)));
    }

    [Fact]
    public void Release_without_a_press_is_ignored()
    {
        var state = new PttKeyState(TargetKey);

        Assert.Equal(PttEdge.None, state.Apply(KeyEvent(TargetKey, EvdevInputEventParser.KeyReleased)));
    }

    [Fact]
    public void Another_key_is_ignored()
    {
        var state = new PttKeyState(TargetKey);

        Assert.Equal(PttEdge.None, state.Apply(KeyEvent(99, EvdevInputEventParser.KeyPressed)));
        Assert.False(state.IsDown);
    }

    [Fact]
    public void A_non_key_event_is_ignored()
    {
        var state = new PttKeyState(TargetKey);

        Assert.Equal(PttEdge.None, state.Apply(new EvdevInputEvent(0x02 /* EV_REL */, TargetKey, 1)));
    }

    [Fact]
    public void The_key_re_arms_after_a_full_cycle()
    {
        var state = new PttKeyState(TargetKey);
        state.Apply(KeyEvent(TargetKey, EvdevInputEventParser.KeyPressed));
        state.Apply(KeyEvent(TargetKey, EvdevInputEventParser.KeyReleased));

        // A second hold must fire again — the property that makes hold-after-hold work.
        Assert.Equal(PttEdge.Pressed, state.Apply(KeyEvent(TargetKey, EvdevInputEventParser.KeyPressed)));
    }
}
