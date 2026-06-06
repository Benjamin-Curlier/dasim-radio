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

    [Fact]
    public void Xdg_x11_is_x11() =>
        Assert.Equal(SessionType.X11, SessionTypeDetector.Detect(Env(("XDG_SESSION_TYPE", "x11"))));

    [Fact]
    public void Wayland_display_without_xdg_is_wayland() =>
        Assert.Equal(SessionType.Wayland, SessionTypeDetector.Detect(Env(("WAYLAND_DISPLAY", "wayland-0"))));

    [Fact]
    public void X11_display_without_xdg_is_x11() =>
        Assert.Equal(SessionType.X11, SessionTypeDetector.Detect(Env(("DISPLAY", ":0"))));

    [Fact]
    public void Nothing_set_is_other() =>
        Assert.Equal(SessionType.Other, SessionTypeDetector.Detect(Env(("XDG_SESSION_TYPE", "tty"))));
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
        Assert.False(EvdevInputEventParser.TryParse(new byte[10], out _));
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
}
