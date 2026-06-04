namespace Dasim.Radio.Core;

/// <summary>Echelon of a node in the force tree, from highest command to individual member.</summary>
public enum ForceNodeKind
{
    Command,
    Company,
    Section,
    Group,
    Member,
}

/// <summary>A node in the force tree (the military hierarchy imported from NATS).</summary>
public sealed record ForceNode(
    string Id,
    string Name,
    ForceNodeKind Kind,
    Priority Priority,
    IReadOnlyList<ForceNode> Children)
{
    /// <summary>Creates a childless node (a leaf, typically a member).</summary>
    public static ForceNode Leaf(string id, string name, ForceNodeKind kind, Priority priority) =>
        new(id, name, kind, priority, []);
}

/// <summary>The full force tree with id-based lookup and traversal.</summary>
public sealed class ForceTree
{
    private readonly Dictionary<string, ForceNode> _byId;

    public ForceTree(ForceNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Root = root;
        _byId = new Dictionary<string, ForceNode>(StringComparer.Ordinal);
        Index(root);
    }

    public ForceNode Root { get; }

    /// <summary>Returns the node with the given id, or <c>null</c> if it is not in the tree.</summary>
    public ForceNode? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Enumerates every node depth-first, starting at the root.</summary>
    public IEnumerable<ForceNode> Enumerate()
    {
        var stack = new Stack<ForceNode>();
        stack.Push(Root);
        while (stack.Count > 0)
        {
            ForceNode node = stack.Pop();
            yield return node;
            for (int i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(node.Children[i]);
            }
        }
    }

    private void Index(ForceNode node)
    {
        if (!_byId.TryAdd(node.Id, node))
        {
            throw new ArgumentException($"Duplicate force node id '{node.Id}'.", nameof(node));
        }

        foreach (ForceNode child in node.Children)
        {
            Index(child);
        }
    }
}
