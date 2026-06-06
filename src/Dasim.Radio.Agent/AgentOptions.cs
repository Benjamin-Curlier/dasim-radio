namespace Dasim.Radio.Agent;

/// <summary>
/// Configuration for the host agent, bound from the <c>Agent</c> configuration section.
/// </summary>
public sealed class AgentOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Agent";

    /// <summary>
    /// Stable identity of this post. Used as the <c>agent.&lt;hostId&gt;.cmd</c> subject token and
    /// the <c>presence</c> KV key, so it MUST be a single NATS token (no <c>.</c>, <c>*</c>, <c>&gt;</c>
    /// or whitespace).
    /// </summary>
    public string HostId { get; set; } = string.Empty;

    /// <summary>Human-friendly host name surfaced to the manager for display.</summary>
    public string HostName { get; set; } = string.Empty;

    /// <summary>LAN address surfaced in presence heartbeats (informational; may be empty).</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// How often a presence heartbeat is broadcast. Kept well under the <c>presence</c> bucket TTL
    /// (15s) so a live post never expires between beats. Must be positive.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Path to the client executable the agent launches on a <c>launch</c>/<c>reconfigure</c> command.
    /// The client host is built separately; until then this is config-driven and forward-compatible.
    /// </summary>
    public string ClientExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Whether a <c>launch</c> command may replace an already-running client. When <c>false</c> (the
    /// default) a launch is rejected while a client is running; the manager must <c>stop</c> first.
    /// </summary>
    public bool AllowReplaceRunningClient { get; set; }
}
