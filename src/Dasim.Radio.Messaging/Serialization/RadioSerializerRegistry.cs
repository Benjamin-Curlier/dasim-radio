using System.Buffers;
using System.Text.Json.Serialization;
using NATS.Client.Core;

namespace Dasim.Radio.Messaging.Serialization;

/// <summary>
/// The single source of truth for the Dasim.Radio wire format: raw bytes for the audio data
/// plane (no JSON on the 20 ms hot path) and source-generated System.Text.Json for the
/// control-plane DTOs. Every NATS connection in the stack is configured with this registry
/// (see <see cref="RadioNatsOpts"/>).
/// </summary>
public sealed class RadioSerializerRegistry : INatsSerializerRegistry
{
    /// <summary>Shared, stateless instance.</summary>
    public static readonly RadioSerializerRegistry Default = new();

    private static readonly JsonSerializerContext[] JsonContexts = [RadioJsonContext.Default];

    public INatsSerialize<T> GetSerializer<T>() => SerializerFor<T>.Instance;

    public INatsDeserialize<T> GetDeserializer<T>() => SerializerFor<T>.Instance;

    // Resolved once per T by the runtime, so there is no per-message cost.
    private static class SerializerFor<T>
    {
        public static readonly INatsSerializer<T> Instance =
            IsRawBytes(typeof(T))
                ? NatsDefaultSerializer<T>.Default
                : new NatsJsonContextSerializer<T>(JsonContexts, NatsDefaultSerializer<T>.Default);
    }

    private static bool IsRawBytes(Type type) =>
        type == typeof(byte[])
        || type == typeof(Memory<byte>)
        || type == typeof(ReadOnlyMemory<byte>)
        || type == typeof(ReadOnlySequence<byte>)
        || type == typeof(NatsMemoryOwner<byte>);
}
