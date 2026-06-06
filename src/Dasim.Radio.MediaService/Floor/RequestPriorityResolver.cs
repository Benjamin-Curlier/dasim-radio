using Dasim.Radio.Core;
using Microsoft.Extensions.Logging;

namespace Dasim.Radio.MediaService.Floor;

/// <summary>
/// Interim resolver that trusts the priority the client put on the wire.
/// <para>
/// ⚠️ Security gap by design: a client could claim a higher priority than its rank and pre-empt a
/// superior. This is acceptable only until the force tree is available — it must be replaced by a
/// resolver that derives priority from the authoritative <c>force_tree</c> bucket (tracked with the
/// per-listener routing work, which loads the tree anyway). It logs a warning on construction so the
/// gap is never silently the deployed behaviour.
/// </para>
/// </summary>
public sealed class RequestPriorityResolver : IFloorPriorityResolver
{
    public RequestPriorityResolver(ILogger<RequestPriorityResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        logger.LogWarning(
            "Floor priority is TRUSTING client-sent values: a client can claim a higher rank than it " +
            "holds and pre-empt a superior. Replace with a force-tree-derived resolver before production.");
    }

    public ValueTask<Priority> ResolveAsync(
        ParticipantId participant, Priority requested, CancellationToken cancellationToken = default) =>
        new(requested);
}
