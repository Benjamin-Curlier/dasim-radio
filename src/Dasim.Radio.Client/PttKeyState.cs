namespace Dasim.Radio.Client;

/// <summary>A push-to-talk edge derived from a key event.</summary>
public enum PttEdge
{
    /// <summary>No PTT transition (not our key, an auto-repeat, or a redundant press/release).</summary>
    None,

    /// <summary>The PTT key went down.</summary>
    Pressed,

    /// <summary>The PTT key went up.</summary>
    Released,
}

/// <summary>
/// Tracks the down/up state of one configured PTT key and reports only genuine transitions. Filters
/// auto-repeats and redundant events so the evdev provider raises exactly one press and one release per
/// hold — the debounce logic the native read loop relies on. Pure and confined to a single reader.
/// </summary>
public sealed class PttKeyState(ushort keyCode)
{
    private bool _down;

    /// <summary>The evdev key code this state tracks.</summary>
    public ushort KeyCode => keyCode;

    /// <summary>Whether the key is currently held down.</summary>
    public bool IsDown => _down;

    /// <summary>Feeds one input event and returns the PTT edge it produced, if any.</summary>
    public PttEdge Apply(EvdevInputEvent input)
    {
        if (input.Type != EvdevInputEventParser.EventTypeKey || input.Code != keyCode)
        {
            return PttEdge.None;
        }

        switch (input.Value)
        {
            case EvdevInputEventParser.KeyPressed when !_down:
                _down = true;
                return PttEdge.Pressed;
            case EvdevInputEventParser.KeyReleased when _down:
                _down = false;
                return PttEdge.Released;
            default:
                return PttEdge.None; // auto-repeat, or a press/release we're already in
        }
    }
}
