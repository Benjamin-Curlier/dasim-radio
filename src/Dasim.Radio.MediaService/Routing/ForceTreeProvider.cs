using Dasim.Radio.Contracts;
using Dasim.Radio.Core;
using Dasim.Radio.Messaging.KeyValue;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dasim.Radio.MediaService.Routing;

/// <summary>
/// Keeps the current <see cref="ForceRouting"/> in step with the <c>force_tree</c> KV bucket: watches
/// the authoritative key, rebuilds the topology on each new version, and swaps it in atomically. An
/// invalid tree is rejected without disturbing the one already in use, and a watch fault resubscribes
/// rather than tearing down the host.
/// </summary>
public sealed class ForceTreeProvider : BackgroundService, IForceTreeProvider
{
    private static readonly TimeSpan ResubscribeDelay = TimeSpan.FromSeconds(1);

    private readonly IControlPlaneStore _store;
    private readonly ILogger<ForceTreeProvider> _logger;
    private volatile ForceRouting _current = ForceRouting.Empty;

    public ForceTreeProvider(IControlPlaneStore store, ILogger<ForceTreeProvider> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ForceRouting Current => _current;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Force-tree provider started; watching the force_tree bucket.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                INatsKeyValueStore<ForceTreeDto> bucket =
                    await _store.ForceTreeAsync(stoppingToken).ConfigureAwait(false);

                // WatchAsync replays the current value first, then streams live Puts.
                await foreach (KeyValueEntry<ForceTreeDto> entry in
                    bucket.WatchAsync(stoppingToken).ConfigureAwait(false))
                {
                    if (string.Equals(entry.Key, Subjects.Keys.ForceTreeCurrent, StringComparison.Ordinal))
                    {
                        Apply(entry.Value);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "force_tree watch faulted; resubscribing.");
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

    private void Apply(ForceTreeDto dto)
    {
        try
        {
            ForceTree tree = ForceTreeMapper.ToDomain(dto);
            NetTopology topology = NetTopology.FromForceTree(tree);
            _current = ForceRouting.Create(dto.Version, tree, topology);
            _logger.LogInformation("Applied force tree v{Version} ({NetCount} nets).", dto.Version, topology.Nets.Count);
        }
        catch (ArgumentException ex)
        {
            // A malformed tree (unknown echelon, duplicate id, bad token) must not unseat a good one.
            _logger.LogWarning(
                ex, "Rejected force tree v{Version}; keeping the previous topology.", dto.Version);
        }
    }
}
