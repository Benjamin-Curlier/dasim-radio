using System.Runtime.CompilerServices;
using NATS.Client.Core;

namespace Dasim.Radio.Messaging.Internal;

/// <summary>Helpers for projecting NATS subscription streams onto their payloads.</summary>
internal static class NatsStreams
{
    /// <summary>Yields the deserialized payload of each message, skipping any with no body.</summary>
    public static async IAsyncEnumerable<T> DataAsync<T>(
        IAsyncEnumerable<NatsMsg<T>> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (NatsMsg<T> msg in source.WithCancellation(cancellationToken))
        {
            if (msg.Data is { } data)
            {
                yield return data;
            }
        }
    }
}
