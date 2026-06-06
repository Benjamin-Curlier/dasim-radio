using Dasim.Radio.Contracts;
using Dasim.Radio.Core;

namespace Dasim.Radio.MediaService.Routing;

/// <summary>
/// Maps the primitive <see cref="ForceTreeDto"/> read from the <c>force_tree</c> KV bucket onto the
/// domain <see cref="ForceTree"/>. Lives here (not in Core or Contracts) because it is the one place
/// that depends on both — Contracts stays primitive-only and Core stays transport-agnostic.
/// </summary>
public static class ForceTreeMapper
{
    /// <summary>Builds a domain force tree from its wire DTO. Throws <see cref="ArgumentException"/> on an unknown echelon.</summary>
    public static ForceTree ToDomain(ForceTreeDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(dto.Root);
        return new ForceTree(ToNode(dto.Root));
    }

    private static ForceNode ToNode(ForceNodeDto dto)
    {
        if (!Enum.TryParse(dto.Kind, ignoreCase: true, out ForceNodeKind kind))
        {
            throw new ArgumentException($"Unknown force node kind '{dto.Kind}' for node '{dto.Id}'.", nameof(dto));
        }

        var children = new ForceNode[dto.Children.Length];
        for (int i = 0; i < dto.Children.Length; i++)
        {
            children[i] = ToNode(dto.Children[i]);
        }

        return new ForceNode(dto.Id, dto.Name, kind, new Priority(dto.Priority), children);
    }
}
