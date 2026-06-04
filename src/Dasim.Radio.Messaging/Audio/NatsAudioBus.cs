using System.Runtime.CompilerServices;
using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.Internal;
using NATS.Client.Core;

namespace Dasim.Radio.Messaging.Audio;

/// <summary>Core-NATS implementation of <see cref="IAudioBus"/>.</summary>
public sealed class NatsAudioBus : IAudioBus
{
    private readonly INatsConnection _connection;

    public NatsAudioBus(INatsConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    public ValueTask PublishCapturedAsync(string clientId, ReadOnlyMemory<byte> opusFrame, CancellationToken cancellationToken = default) =>
        _connection.PublishAsync(Subjects.Audio.In(clientId), opusFrame, cancellationToken: cancellationToken);

    public ValueTask PublishMixedAsync(string listenerClientId, ReadOnlyMemory<byte> opusFrame, CancellationToken cancellationToken = default) =>
        _connection.PublishAsync(Subjects.Audio.Out(listenerClientId), opusFrame, cancellationToken: cancellationToken);

    public async IAsyncEnumerable<AudioFrame> SubscribeCapturedAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (NatsMsg<byte[]> msg in _connection.SubscribeAsync<byte[]>(Subjects.Audio.AllIn, cancellationToken: cancellationToken))
        {
            if (msg.Data is { } frame)
            {
                yield return new AudioFrame(ClientIdFromSubject(msg.Subject), frame);
            }
        }
    }

    public IAsyncEnumerable<byte[]> SubscribeMixedAsync(string clientId, CancellationToken cancellationToken = default) =>
        NatsStreams.DataAsync(_connection.SubscribeAsync<byte[]>(Subjects.Audio.Out(clientId), cancellationToken: cancellationToken), cancellationToken);

    // subject == "audio.in.<clientId>" — the client id is the trailing token.
    private static string ClientIdFromSubject(string subject)
    {
        int lastDot = subject.LastIndexOf('.');
        return lastDot >= 0 ? subject[(lastDot + 1)..] : subject;
    }
}
