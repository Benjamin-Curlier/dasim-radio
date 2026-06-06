using Dasim.Radio.Core;
using Dasim.Radio.Messaging.Audio;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dasim.Radio.MediaService.Routing;

/// <summary>
/// Hosts the data plane: subscribes to every client's captured audio (<c>audio.in.&gt;</c>), asks the
/// <see cref="MediaRouter"/> who should hear each frame, and republishes it to those listeners'
/// <c>audio.out</c>. Deliberately thin — the routing decision lives in (and is tested through) the
/// router. A single bad frame is logged and skipped; a subscription fault resubscribes.
/// </summary>
public sealed class MediaRouterService : BackgroundService
{
    private static readonly TimeSpan ResubscribeDelay = TimeSpan.FromSeconds(1);

    private readonly IAudioBus _audioBus;
    private readonly MediaRouter _router;
    private readonly MixRenderer _renderer;
    private readonly ILogger<MediaRouterService> _logger;

    public MediaRouterService(
        IAudioBus audioBus, MediaRouter router, MixRenderer renderer, ILogger<MediaRouterService> logger)
    {
        _audioBus = audioBus ?? throw new ArgumentNullException(nameof(audioBus));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Media router started; forwarding floor-holder audio per listener.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (AudioFrame frame in
                    _audioBus.SubscribeCapturedAsync(stoppingToken).ConfigureAwait(false))
                {
                    try
                    {
                        await RouteAsync(frame, stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Failed to route a captured frame from {Client}.", frame.ClientId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Captured-audio subscription faulted; resubscribing.");
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

    private async ValueTask RouteAsync(AudioFrame frame, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(frame.ClientId))
        {
            return;
        }

        var speaker = new ParticipantId(frame.ClientId);

        // Buffer this speaker's frame first (a non-trigger source contributes only as a buffered peer),
        // then emit the mixes this frame triggers: pass-through for an undegraded single source, or
        // decode → sum → degrade → re-encode for a multi-source or degraded listener.
        _renderer.Remember(speaker, frame.Opus);

        IReadOnlyList<MixDelivery> deliveries = _router.Deliveries(speaker);
        if (deliveries.Count == 0)
        {
            return;
        }

        // Publishing is sequential on purpose. NATS.Net's PublishAsync does NOT flush the socket per
        // call: its CommandWriter writes the frame into a per-connection Pipe and returns once it is
        // queued (a flush is kicked off asynchronously), so each await here completes synchronously in
        // the common case. It only blocks under connection-wide buffer saturation — which is shared by
        // every subject, so a per-listener fan-out channel could not relieve it; it would only add a
        // second queue and force per-frame copies (the rendered frames alias the renderer's reused
        // scratch, valid only until the next Render). If load testing ever shows the data plane
        // saturating the connection, the right fix is dropping stale audio, not more buffering.
        // Index, don't foreach: Render returns an IReadOnlyList-typed value, and a foreach over that
        // boxes the enumerator on this per-frame path.
        IReadOnlyList<RenderedFrame> rendered = _renderer.Render(deliveries);
        for (int i = 0; i < rendered.Count; i++)
        {
            RenderedFrame mixed = rendered[i];
            await _audioBus.PublishMixedAsync(mixed.Listener.Value, mixed.Opus, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
