using System.Text.Json.Serialization;
using Dasim.Radio.Contracts;

namespace Dasim.Radio.Messaging.Serialization;

/// <summary>
/// Source-generated System.Text.Json metadata for every control-plane wire DTO. Source
/// generation keeps the control plane reflection-free (Native AOT friendly for the agent host)
/// and lists, in one place, exactly which types are allowed on the wire.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FloorRequestMessage))]
[JsonSerializable(typeof(FloorReleaseMessage))]
[JsonSerializable(typeof(FloorEventMessage))]
[JsonSerializable(typeof(PresenceHeartbeat))]
[JsonSerializable(typeof(LaunchClientCommand))]
[JsonSerializable(typeof(StopClientCommand))]
[JsonSerializable(typeof(DegradeCommand))]
[JsonSerializable(typeof(AgentCommandEnvelope))]
[JsonSerializable(typeof(AgentCommandResult))]
[JsonSerializable(typeof(ForceTreeDto))]
[JsonSerializable(typeof(ForceNodeDto))]
[JsonSerializable(typeof(EndpointDto))]
[JsonSerializable(typeof(FloorStateDto))]
public sealed partial class RadioJsonContext : JsonSerializerContext;
