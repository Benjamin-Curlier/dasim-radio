using Dasim.Radio.Contracts;

namespace Dasim.Radio.Manager.Core;

/// <summary>
/// Structural validation of a <see cref="ForceTreeDto"/> before it is imported into the <c>force_tree</c>
/// bucket: every node id must be a single NATS token (ids become net ids / subjects), ids must be unique,
/// and names non-empty. This is DTO-level only; full domain/echelon validation (via the force-tree mapper)
/// is a follow-up that needs a shared Core+Contracts routing library.
/// </summary>
public static class ForceTreeValidator
{
    /// <summary>Returns the validation errors for <paramref name="tree"/>; an empty list means it is valid.</summary>
    public static IReadOnlyList<string> Validate(ForceTreeDto tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var errors = new List<string>();
        if (tree.Root is null)
        {
            errors.Add("The force tree has no root node.");
            return errors;
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        Walk(tree.Root, seenIds, errors);
        return errors;
    }

    /// <summary>
    /// Returns a copy of <paramref name="tree"/> with every null <c>Children</c> array replaced by an empty
    /// one. JSON deserialization yields <c>null</c> children for a node that omits <c>children</c> (a leaf),
    /// even though the DTO type is non-nullable; normalizing on import keeps downstream consumers (the media
    /// service's force-tree mapper) from dereferencing null.
    /// </summary>
    public static ForceTreeDto Normalize(ForceTreeDto tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        return tree.Root is null ? tree : tree with { Root = NormalizeNode(tree.Root) };
    }

    private static ForceNodeDto NormalizeNode(ForceNodeDto node) =>
        node with { Children = [.. (node.Children ?? []).Select(NormalizeNode)] };

    private static void Walk(ForceNodeDto node, HashSet<string> seenIds, List<string> errors)
    {
        if (!NatsToken.IsSingleToken(node.Id))
        {
            errors.Add($"Node id '{node.Id}' must be a single NATS token (no '.', '*', '>' or whitespace).");
        }
        else if (!seenIds.Add(node.Id))
        {
            errors.Add($"Duplicate node id '{node.Id}'.");
        }

        if (string.IsNullOrWhiteSpace(node.Name))
        {
            errors.Add($"Node '{node.Id}' has an empty name.");
        }

        foreach (ForceNodeDto child in node.Children ?? [])
        {
            Walk(child, seenIds, errors);
        }
    }
}
