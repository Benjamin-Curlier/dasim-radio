using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.Internal;
using NATS.Client.Core;

namespace Dasim.Radio.Messaging.Floor;

/// <summary>Core-NATS implementation of <see cref="IFloorSignal"/>.</summary>
public sealed class NatsFloorSignal : IFloorSignal
{
    private readonly INatsConnection _connection;

    public NatsFloorSignal(INatsConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    public ValueTask RequestAsync(FloorRequestMessage request, CancellationToken cancellationToken = default) =>
        _connection.PublishAsync(Subjects.Floor.Request, request, cancellationToken: cancellationToken);

    public ValueTask ReleaseAsync(FloorReleaseMessage release, CancellationToken cancellationToken = default) =>
        _connection.PublishAsync(Subjects.Floor.Release, release, cancellationToken: cancellationToken);

    public ValueTask PublishEventAsync(FloorEventMessage @event, CancellationToken cancellationToken = default) =>
        _connection.PublishAsync(Subjects.Floor.Events(@event.NetId), @event, cancellationToken: cancellationToken);

    public IAsyncEnumerable<FloorRequestMessage> SubscribeRequestsAsync(CancellationToken cancellationToken = default) =>
        NatsStreams.DataAsync(_connection.SubscribeAsync<FloorRequestMessage>(Subjects.Floor.Request, cancellationToken: cancellationToken), cancellationToken);

    public IAsyncEnumerable<FloorReleaseMessage> SubscribeReleasesAsync(CancellationToken cancellationToken = default) =>
        NatsStreams.DataAsync(_connection.SubscribeAsync<FloorReleaseMessage>(Subjects.Floor.Release, cancellationToken: cancellationToken), cancellationToken);

    public IAsyncEnumerable<FloorEventMessage> SubscribeEventsAsync(string netId, CancellationToken cancellationToken = default) =>
        NatsStreams.DataAsync(_connection.SubscribeAsync<FloorEventMessage>(Subjects.Floor.Events(netId), cancellationToken: cancellationToken), cancellationToken);
}
