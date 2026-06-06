namespace Dasim.Radio.Audio;

/// <summary>
/// Reframes a stream of float PCM samples arriving in arbitrary chunk sizes (as a polling capture
/// backend like OwnAudioSharp delivers them) into fixed-size 16-bit frames (e.g. one 20 ms Opus frame
/// = 960 samples), invoking a handler for each complete frame. Confine to a single producer; the
/// emitted span is valid only for the duration of the handler call (copy out anything you keep).
/// </summary>
public sealed class PcmFrameAccumulator
{
    private readonly short[] _frame;
    private int _filled;

    public PcmFrameAccumulator(int frameSamples)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frameSamples, 1);
        _frame = new short[frameSamples];
    }

    /// <summary>Samples buffered toward the next frame (0 .. frame size - 1).</summary>
    public int Pending => _filled;

    /// <summary>
    /// Appends <paramref name="samples"/> (converted from float to 16-bit), emitting each complete frame
    /// to <paramref name="onFrame"/>. Leftover samples are retained for the next call.
    /// </summary>
    public void Append(ReadOnlySpan<float> samples, AudioFrameHandler onFrame)
    {
        ArgumentNullException.ThrowIfNull(onFrame);

        foreach (float sample in samples)
        {
            _frame[_filled++] = PcmConvert.ToShort(sample);
            if (_filled == _frame.Length)
            {
                _filled = 0;
                onFrame(_frame);
            }
        }
    }

    /// <summary>Discards any buffered partial frame (e.g. on stop, so the next session starts aligned).</summary>
    public void Reset() => _filled = 0;
}
