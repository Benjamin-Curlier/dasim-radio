using Dasim.Radio.Core;

namespace Dasim.Radio.MediaService.Floor;

/// <summary>
/// Resolves the transmission <see cref="Priority"/> the floor authority should arbitrate with for a
/// given request. This is a deliberate seam: the floor decision is only as trustworthy as the
/// priority feeding it, and in a chain-of-command system that priority must ultimately come from the
/// authoritative force tree — not from whatever the client claims.
/// </summary>
public interface IFloorPriorityResolver
{
    /// <summary>
    /// Returns the priority to arbitrate with for <paramref name="participant"/>, given the
    /// <paramref name="requested"/> priority carried on the wire.
    /// </summary>
    ValueTask<Priority> ResolveAsync(
        ParticipantId participant, Priority requested, CancellationToken cancellationToken = default);
}
