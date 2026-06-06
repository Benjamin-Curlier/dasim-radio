namespace Dasim.Radio.Manager.Core;

/// <summary>Configuration for the manager, bound from the <c>Manager</c> configuration section.</summary>
public sealed class ManagerOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Manager";

    /// <summary>
    /// A post is considered offline once its last heartbeat is older than this. Matches the
    /// <c>presence</c> bucket TTL (15s) by default. Must be positive.
    /// </summary>
    public TimeSpan PresenceStaleAfter { get; set; } = TimeSpan.FromSeconds(15);
}
