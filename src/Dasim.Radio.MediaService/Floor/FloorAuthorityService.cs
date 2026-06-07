using System.Threading.Channels;
using Dasim.Radio.Messaging.Floor;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dasim.Radio.MediaService.Floor;

/// <summary>
/// Hosts the floor authority: subscribes to <c>floor.request</c> and <c>floor.release</c> on core
/// NATS and funnels both streams through a SINGLE consumer that drives the <see cref="FloorArbiter"/>.
/// Serializing the side effects keeps each net's events and <c>floor_state</c> writes in decision
/// order (the decision itself is already serialized per net inside <c>FloorControlService</c>).
/// <para>
/// The two producer loops drain independent subscriptions, so a request and a release for the SAME
/// participant can still be enqueued out of order relative to each other. That cross-stream reorder is
/// made safe NOT by this single consumer but by the per-participant press sequence carried on the wire
/// and enforced in <c>FloorControlService</c> (a release that doesn't match the held press is rejected).
/// </para>
/// Deliberately thin — all the decision logic lives in (and is tested through) the arbiter.
/// </summary>
public sealed class FloorAuthorityService(
    IFloorSignal signal, FloorArbiter arbiter, ILogger<FloorAuthorityService> logger) : BackgroundService
{
    private static readonly TimeSpan ResubscribeDelay = TimeSpan.FromSeconds(1);

    private readonly IFloorSignal _signal = signal ?? throw new ArgumentNullException(nameof(signal));
    private readonly FloorArbiter _arbiter = arbiter ?? throw new ArgumentNullException(nameof(arbiter));
    private readonly ILogger<FloorAuthorityService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Floor authority started; arbitrating floor.request and floor.release.");

        Channel<Func<CancellationToken, ValueTask>> work =
            Channel.CreateUnbounded<Func<CancellationToken, ValueTask>>(new UnboundedChannelOptions { SingleReader = true });

        Task consumer = ConsumeAsync(work.Reader, stoppingToken);
        try
        {
            await Task.WhenAll(
                ProduceAsync("floor.request", _signal.SubscribeRequestsAsync,
                    message => ct => _arbiter.HandleRequestAsync(message, ct), work.Writer, stoppingToken),
                ProduceAsync("floor.release", _signal.SubscribeReleasesAsync,
                    message => ct => _arbiter.HandleReleaseAsync(message, ct), work.Writer, stoppingToken))
                .ConfigureAwait(false);
        }
        finally
        {
            work.Writer.TryComplete();
            await consumer.ConfigureAwait(false);
        }
    }

    private async Task ProduceAsync<T>(
        string subject,
        Func<CancellationToken, IAsyncEnumerable<T>> subscribe,
        Func<T, Func<CancellationToken, ValueTask>> toWork,
        ChannelWriter<Func<CancellationToken, ValueTask>> writer,
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (T message in subscribe(stoppingToken).WithCancellation(stoppingToken).ConfigureAwait(false))
                {
                    await writer.WriteAsync(toWork(message), stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A transient subscription fault (e.g. a NATS drop) must not tear down the host or the
                // other stream — log and resubscribe after a short delay.
                _logger.LogError(ex, "Subscription to {Subject} faulted; resubscribing.", subject);
                try
                {
                    await Task.Delay(ResubscribeDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task ConsumeAsync(
        ChannelReader<Func<CancellationToken, ValueTask>> reader, CancellationToken stoppingToken)
    {
        try
        {
            await foreach (Func<CancellationToken, ValueTask> handle in reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await handle(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to handle a floor signal.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }
}
