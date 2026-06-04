using Dasim.Radio.Contracts;

namespace Dasim.Radio.Messaging.Degrade;

/// <summary>
/// Per-listener quality/clarity degradation commands over core NATS (<c>cmd.degrade</c>). The
/// manager publishes; the media service consumes and applies the degradation to that listener's
/// mix. Fire-and-forget signalling — there is no reply.
/// </summary>
public interface IDegradeChannel
{
    /// <summary>Publishes a degradation command — manager side.</summary>
    ValueTask PublishAsync(DegradeCommand command, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to all degradation commands — media service side.</summary>
    IAsyncEnumerable<DegradeCommand> SubscribeAsync(CancellationToken cancellationToken = default);
}
