using Dasim.Radio.Contracts;

namespace Dasim.Radio.MediaService.Floor;

/// <summary>
/// Durably records the authoritative floor state for a net (the <c>floor_state</c> KV bucket), so
/// late-joining clients and the manager can observe who holds each net. This is the snapshot path,
/// not the real-time signal — clients react to <see cref="FloorEventMessage"/>s; this only has to be
/// eventually consistent with them.
/// </summary>
public interface IFloorStateWriter
{
    /// <summary>Records the current floor state of one net.</summary>
    ValueTask WriteAsync(FloorStateDto state, CancellationToken cancellationToken = default);
}
