using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.Internal;
using NATS.Client.Core;

namespace Dasim.Radio.Messaging.Presence;

/// <summary>Core-NATS implementation of <see cref="IPresenceChannel"/>.</summary>
public sealed class NatsPresenceChannel : IPresenceChannel
{
    private readonly INatsConnection _connection;

    public NatsPresenceChannel(INatsConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    public ValueTask PublishAsync(PresenceHeartbeat heartbeat, CancellationToken cancellationToken = default) =>
        _connection.PublishAsync(Subjects.Control.Presence, heartbeat, cancellationToken: cancellationToken);

    public IAsyncEnumerable<PresenceHeartbeat> SubscribeAsync(CancellationToken cancellationToken = default) =>
        NatsStreams.DataAsync(_connection.SubscribeAsync<PresenceHeartbeat>(Subjects.Control.Presence, cancellationToken: cancellationToken), cancellationToken);
}
