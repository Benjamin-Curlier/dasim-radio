namespace Dasim.Radio.Client;

/// <summary>
/// Merges several push-to-talk sources (e.g. an on-screen button plus a global hotkey) into one: it
/// transmits while <em>any</em> source is held. A reference count collapses overlapping holds into a
/// single <see cref="Pressed"/> (on the first source down) and a single <see cref="Released"/> (when the
/// last source comes up), so an operator can use whichever input is convenient without double-keying the
/// floor. Owns its sources — <see cref="Start"/>/<see cref="Stop"/>/<see cref="Dispose"/> fan out to them.
/// </summary>
public sealed class CompositePushToTalk : IPushToTalkHotkey
{
    private readonly IReadOnlyList<IPushToTalkHotkey> _sources;
    private readonly Lock _gate = new();
    private int _heldCount;
    private bool _disposed;

    public CompositePushToTalk(params IPushToTalkHotkey[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Length == 0)
        {
            throw new ArgumentException("At least one push-to-talk source is required.", nameof(sources));
        }

        _sources = sources;
        foreach (IPushToTalkHotkey source in _sources)
        {
            source.Pressed += OnSourcePressed;
            source.Released += OnSourceReleased;
        }
    }

    public event Action? Pressed;

    public event Action? Released;

    public void Start()
    {
        foreach (IPushToTalkHotkey source in _sources)
        {
            source.Start();
        }
    }

    public void Stop()
    {
        foreach (IPushToTalkHotkey source in _sources)
        {
            source.Stop();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (IPushToTalkHotkey source in _sources)
        {
            source.Pressed -= OnSourcePressed;
            source.Released -= OnSourceReleased;
            source.Dispose();
        }
    }

    private void OnSourcePressed()
    {
        bool transmit;
        lock (_gate)
        {
            transmit = ++_heldCount == 1; // first source down
        }

        if (transmit)
        {
            Pressed?.Invoke();
        }
    }

    private void OnSourceReleased()
    {
        bool stop;
        lock (_gate)
        {
            // Guard against an unmatched release (a source that releases without a tracked press).
            stop = _heldCount > 0 && --_heldCount == 0;
        }

        if (stop)
        {
            Released?.Invoke();
        }
    }
}
