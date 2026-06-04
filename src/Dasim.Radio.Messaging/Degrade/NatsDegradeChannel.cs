using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.Internal;
using NATS.Client.Core;

namespace Dasim.Radio.Messaging.Degrade;

/// <summary>Core-NATS implementation of <see cref="IDegradeChannel"/>.</summary>
public sealed class NatsDegradeChannel : IDegradeChannel
{
    private readonly INatsConnection _connection;

    public NatsDegradeChannel(INatsConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    public ValueTask PublishAsync(DegradeCommand command, CancellationToken cancellationToken = default) =>
        _connection.PublishAsync(Subjects.Control.Degrade, command, cancellationToken: cancellationToken);

    public IAsyncEnumerable<DegradeCommand> SubscribeAsync(CancellationToken cancellationToken = default) =>
        NatsStreams.DataAsync(_connection.SubscribeAsync<DegradeCommand>(Subjects.Control.Degrade, cancellationToken: cancellationToken), cancellationToken);
}
