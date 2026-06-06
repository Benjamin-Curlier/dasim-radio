using System.Collections.Concurrent;

namespace Dasim.Radio.Client;

/// <summary>
/// A tiny free-list of fixed-size PCM buffers shared between the realtime capture thread (which rents
/// and fills one per frame) and the transmit pump (which encodes and returns it). Keeps the capture
/// hot path allocation-free after warm-up; both <see cref="Rent"/> and <see cref="Return"/> are
/// lock-free and non-blocking, so they are safe to call from the audio thread.
/// </summary>
internal sealed class FramePool(int frameSamples)
{
    private readonly ConcurrentQueue<short[]> _free = new();
    private readonly int _frameSamples = frameSamples;

    public short[] Rent() => _free.TryDequeue(out short[]? buffer) ? buffer : new short[_frameSamples];

    public void Return(short[] buffer) => _free.Enqueue(buffer);

    /// <summary>
    /// Pre-allocates <paramref name="count"/> buffers up front. The working set is bounded by the
    /// capture channel's capacity (rent → enqueue → encode → return is 1:1), so the free-list does not
    /// grow past it; pre-warming just keeps the first talk-spurt off the allocator.
    /// </summary>
    public void Prewarm(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _free.Enqueue(new short[_frameSamples]);
        }
    }
}
