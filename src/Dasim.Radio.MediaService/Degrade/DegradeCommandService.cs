using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.Degrade;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dasim.Radio.MediaService.Degrade;

/// <summary>
/// Keeps the <see cref="IDegradeRegistry"/> in step with the <c>cmd.degrade</c> stream: each command
/// the manager publishes updates that listener's profile. Thin and resilient — a subscription fault
/// resubscribes rather than tearing down the host.
/// </summary>
public sealed class DegradeCommandService : BackgroundService
{
    private static readonly TimeSpan ResubscribeDelay = TimeSpan.FromSeconds(1);

    private readonly IDegradeChannel _channel;
    private readonly IDegradeRegistry _registry;
    private readonly ILogger<DegradeCommandService> _logger;

    public DegradeCommandService(
        IDegradeChannel channel, IDegradeRegistry registry, ILogger<DegradeCommandService> logger)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Degrade command service started; applying cmd.degrade to listeners.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (DegradeCommand command in
                    _channel.SubscribeAsync(stoppingToken).ConfigureAwait(false))
                {
                    _registry.Apply(command);
                    _logger.LogInformation(
                        "Degrade applied to {Listener}: quality {Quality}%, clarity {Clarity}%.",
                        command.TargetClientId, command.QualityPercent, command.ClarityPercent);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "cmd.degrade subscription faulted; resubscribing.");
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
}
