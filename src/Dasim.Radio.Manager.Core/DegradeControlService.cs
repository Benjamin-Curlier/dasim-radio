using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.Degrade;
using Microsoft.Extensions.Logging;

namespace Dasim.Radio.Manager.Core;

/// <summary>Sends per-listener degrade commands (quality/clarity, optionally scoped to a net).</summary>
public interface IDegradeControlService
{
    /// <summary>Degrades a listener's reception. <paramref name="qualityPercent"/>/<paramref name="clarityPercent"/> are clamped to 0–100.</summary>
    ValueTask DegradeAsync(
        string targetClientId,
        int qualityPercent,
        int clarityPercent,
        string? netId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Restores a listener to full quality/clarity (100/100 — a pass-through).</summary>
    ValueTask ResetAsync(string targetClientId, CancellationToken cancellationToken = default);
}

/// <summary>Implementation of <see cref="IDegradeControlService"/> over <see cref="IDegradeChannel"/>.</summary>
public sealed class DegradeControlService(IDegradeChannel channel, ILogger<DegradeControlService> logger)
    : IDegradeControlService
{
    private readonly IDegradeChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));
    private readonly ILogger<DegradeControlService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public ValueTask DegradeAsync(
        string targetClientId,
        int qualityPercent,
        int clarityPercent,
        string? netId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetClientId);

        // The media service interpolates these into audio.out.<clientId> / a net subject, so reject any value
        // that isn't a single NATS token before publishing.
        NatsToken.EnsureSingleToken(targetClientId, nameof(targetClientId));
        if (netId is not null)
        {
            NatsToken.EnsureSingleToken(netId, nameof(netId));
        }

        int quality = Math.Clamp(qualityPercent, 0, 100);
        int clarity = Math.Clamp(clarityPercent, 0, 100);
        _logger.LogInformation(
            "Degrading '{Target}'{Net} to quality {Quality}/clarity {Clarity}.",
            targetClientId, netId is null ? string.Empty : $" on net '{netId}'", quality, clarity);

        return _channel.PublishAsync(new DegradeCommand(targetClientId, netId, quality, clarity), cancellationToken);
    }

    public ValueTask ResetAsync(string targetClientId, CancellationToken cancellationToken = default) =>
        DegradeAsync(targetClientId, 100, 100, netId: null, cancellationToken);
}
