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

    /// <summary>
    /// Like <see cref="DataAsync{T}(IAsyncEnumerable{NatsMsg{T}}, CancellationToken)"/>, but establishes
    /// the subscription up front via <c>SubscribeCoreAsync</c> and invokes <paramref name="onSubscribed"/>
    /// once it is live on the server — unlike the lazy <c>SubscribeAsync</c> wrapper, where the SUB is
    /// only sent on first enumeration. Lets a caller await registration before publishing on a subject
    /// whose reply rides the same (un-replayed) core-NATS stream.
    /// </summary>
    public static async IAsyncEnumerable<T> DataAsync<T>(
        INatsConnection connection,
        string subject,
        Action? onSubscribed,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using INatsSub<T> sub = await connection
            .SubscribeCoreAsync<T>(subject, cancellationToken: cancellationToken).ConfigureAwait(false);
        onSubscribed?.Invoke();

        await foreach (NatsMsg<T> msg in sub.Msgs.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (msg.Data is { } data)
            {
                yield return data;
            }
        }
    }
}
