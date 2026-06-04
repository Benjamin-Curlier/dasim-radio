using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.Serialization;
using NATS.Client.Core;
using Xunit;

namespace Dasim.Radio.Integration.Tests;

/// <summary>Pure tests for the wire-format registry (no broker).</summary>
public sealed class SerializationTests
{
    [Fact]
    public void Registry_keeps_audio_payloads_off_the_json_path()
    {
        RadioSerializerRegistry registry = RadioSerializerRegistry.Default;

        Assert.IsNotType<NatsJsonContextSerializer<byte[]>>(registry.GetSerializer<byte[]>());
        Assert.IsNotType<NatsJsonContextSerializer<ReadOnlyMemory<byte>>>(registry.GetSerializer<ReadOnlyMemory<byte>>());
        Assert.IsNotType<NatsJsonContextSerializer<byte[]>>(registry.GetDeserializer<byte[]>());

        // Resolved once per T (no per-message allocation).
        Assert.Same(registry.GetSerializer<byte[]>(), registry.GetSerializer<byte[]>());
    }

    [Fact]
    public void Registry_serializes_control_dtos_with_the_source_generated_context()
    {
        RadioSerializerRegistry registry = RadioSerializerRegistry.Default;

        Assert.IsType<NatsJsonContextSerializer<FloorRequestMessage>>(registry.GetSerializer<FloorRequestMessage>());
        Assert.IsType<NatsJsonContextSerializer<DegradeCommand>>(registry.GetDeserializer<DegradeCommand>());
        Assert.IsType<NatsJsonContextSerializer<ForceTreeDto>>(registry.GetSerializer<ForceTreeDto>());
    }
}
