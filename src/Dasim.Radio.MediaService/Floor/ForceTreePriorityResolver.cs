using Dasim.Radio.Core;
using Dasim.Radio.MediaService.Routing;
using Microsoft.Extensions.Logging;

namespace Dasim.Radio.MediaService.Floor;

/// <summary>
/// The authoritative <see cref="IFloorPriorityResolver"/>: a participant's transmission priority is
/// the priority on its <c>force_tree</c> node, never the value the client put on the wire (which it
/// could inflate to pre-empt a superior). Replaces the interim <see cref="RequestPriorityResolver"/>.
/// An unknown participant resolves to the lowest possible priority so it can never pre-empt anyone —
/// and the router will not deliver its audio either, since it is on no net.
/// </summary>
public sealed class ForceTreePriorityResolver : IFloorPriorityResolver
{
    private static readonly Priority Lowest = new(int.MinValue);

    private readonly IForceTreeProvider _provider;
    private readonly ILogger<ForceTreePriorityResolver> _logger;

    public ForceTreePriorityResolver(IForceTreeProvider provider, ILogger<ForceTreePriorityResolver> logger)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ValueTask<Priority> ResolveAsync(
        ParticipantId participant, Priority requested, CancellationToken cancellationToken = default)
    {
        ForceTree? tree = _provider.Current.Tree;
        if (tree?.Find(participant.Value) is { } node)
        {
            return new ValueTask<Priority>(node.Priority);
        }

        _logger.LogWarning(
            "No force-tree entry for participant {Participant}; resolving lowest priority (cannot pre-empt).",
            participant.Value);
        return new ValueTask<Priority>(Lowest);
    }
}
