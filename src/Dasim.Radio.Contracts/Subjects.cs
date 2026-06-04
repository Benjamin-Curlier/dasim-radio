namespace Dasim.Radio.Contracts;

/// <summary>
/// Canonical NATS subjects and JetStream KV bucket names shared by every host.
/// The data plane uses core NATS (ephemeral, low latency); the control plane uses
/// JetStream / KV / Services (persisted, request/reply).
/// </summary>
public static class Subjects
{
    /// <summary>
    /// Audio data plane (core NATS, never JetStream). A <c>clientId</c> must be a single NATS
    /// subject token: no <c>.</c>, <c>*</c> or <c>&gt;</c> (the media service recovers it as the
    /// trailing token of <c>audio.in.&lt;clientId&gt;</c>).
    /// </summary>
    public static class Audio
    {
        /// <summary>Raw Opus frames captured and published by a client.</summary>
        public static string In(string clientId) => $"audio.in.{clientId}";

        /// <summary>Per-listener mixed and degraded stream produced by the media service.</summary>
        public static string Out(string clientId) => $"audio.out.{clientId}";

        /// <summary>Wildcard the media service subscribes to for every client's captured audio.</summary>
        public const string AllIn = "audio.in.>";
    }

    /// <summary>Floor (push-to-talk) control plane.</summary>
    public static class Floor
    {
        public const string Request = "floor.request";
        public const string Release = "floor.release";

        /// <summary>Floor decisions broadcast for a given net.</summary>
        public static string Events(string netId) => $"floor.events.{netId}";
    }

    /// <summary>Command and control plane (manager &lt;-&gt; agents/clients).</summary>
    public static class Control
    {
        /// <summary>Commands targeting a host agent (launch/stop/reconfigure).</summary>
        public static string AgentCommand(string hostId) => $"agent.{hostId}.cmd";

        public const string Presence = "presence.heartbeat";

        /// <summary>Quality/clarity degradation commands (per listener).</summary>
        public const string Degrade = "cmd.degrade";
    }

    /// <summary>JetStream KV buckets (persisted control-plane state).</summary>
    public static class Buckets
    {
        public const string ForceTree = "force_tree";
        public const string Endpoints = "endpoints";
        public const string Configs = "configs";
        public const string Presence = "presence";
        public const string FloorState = "floor_state";
    }
}
