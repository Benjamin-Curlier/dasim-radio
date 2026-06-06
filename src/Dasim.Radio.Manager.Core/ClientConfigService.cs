using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.KeyValue;
using Microsoft.Extensions.Logging;

namespace Dasim.Radio.Manager.Core;

/// <summary>A stored client config together with its KV revision (for optimistic concurrency).</summary>
public sealed record ClientConfigEntry(ClientConfigDto Config, ulong Revision);

/// <summary>CRUD over the <c>configs</c> bucket: the client launch configurations the manager authors.</summary>
public interface IClientConfigService
{
    /// <summary>Lists all stored configs with their revisions.</summary>
    ValueTask<IReadOnlyList<ClientConfigEntry>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads one config, or <c>null</c> if it does not exist.</summary>
    ValueTask<ClientConfigEntry?> GetAsync(string configId, CancellationToken cancellationToken = default);

    /// <summary>Creates a new config; fails if <see cref="ClientConfigDto.ConfigId"/> already exists.</summary>
    ValueTask CreateAsync(ClientConfigDto config, CancellationToken cancellationToken = default);

    /// <summary>Updates a config only if its current revision matches <paramref name="revision"/>.</summary>
    ValueTask UpdateAsync(ClientConfigDto config, ulong revision, CancellationToken cancellationToken = default);

    /// <summary>Deletes a config.</summary>
    ValueTask DeleteAsync(string configId, CancellationToken cancellationToken = default);
}

/// <summary>JetStream-KV implementation of <see cref="IClientConfigService"/> over the <c>configs</c> bucket.</summary>
public sealed class ClientConfigService(IControlPlaneStore store, ILogger<ClientConfigService> logger)
    : IClientConfigService
{
    private readonly IControlPlaneStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ILogger<ClientConfigService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async ValueTask<IReadOnlyList<ClientConfigEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        INatsKeyValueStore<ClientConfigDto> bucket = await BucketAsync(cancellationToken).ConfigureAwait(false);

        var entries = new List<ClientConfigEntry>();
        await foreach (string key in bucket.GetKeysAsync(cancellationToken).ConfigureAwait(false))
        {
            KeyValueEntry<ClientConfigDto>? entry = await bucket.TryGetAsync(key, cancellationToken).ConfigureAwait(false);
            if (entry is { } found)
            {
                entries.Add(new ClientConfigEntry(found.Value, found.Revision));
            }
        }

        return entries;
    }

    public async ValueTask<ClientConfigEntry?> GetAsync(string configId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configId);
        INatsKeyValueStore<ClientConfigDto> bucket = await BucketAsync(cancellationToken).ConfigureAwait(false);
        KeyValueEntry<ClientConfigDto>? entry = await bucket.TryGetAsync(configId, cancellationToken).ConfigureAwait(false);
        return entry is { } found ? new ClientConfigEntry(found.Value, found.Revision) : null;
    }

    public async ValueTask CreateAsync(ClientConfigDto config, CancellationToken cancellationToken = default)
    {
        Validate(config);
        INatsKeyValueStore<ClientConfigDto> bucket = await BucketAsync(cancellationToken).ConfigureAwait(false);
        await bucket.CreateAsync(config.ConfigId, config, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Created client config '{ConfigId}'.", config.ConfigId);
    }

    public async ValueTask UpdateAsync(ClientConfigDto config, ulong revision, CancellationToken cancellationToken = default)
    {
        Validate(config);
        INatsKeyValueStore<ClientConfigDto> bucket = await BucketAsync(cancellationToken).ConfigureAwait(false);
        await bucket.UpdateAsync(config.ConfigId, config, revision, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Updated client config '{ConfigId}'.", config.ConfigId);
    }

    public async ValueTask DeleteAsync(string configId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configId);
        INatsKeyValueStore<ClientConfigDto> bucket = await BucketAsync(cancellationToken).ConfigureAwait(false);
        await bucket.DeleteAsync(configId, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Deleted client config '{ConfigId}'.", configId);
    }

    private ValueTask<INatsKeyValueStore<ClientConfigDto>> BucketAsync(CancellationToken cancellationToken) =>
        _store.BucketAsync<ClientConfigDto>(Subjects.Buckets.Configs, cancellationToken);

    private static void Validate(ClientConfigDto config)
    {
        ArgumentNullException.ThrowIfNull(config);
        NatsToken.EnsureSingleToken(config.ConfigId, nameof(config.ConfigId));
        NatsToken.EnsureSingleToken(config.ClientId, nameof(config.ClientId));
        NatsToken.EnsureSingleToken(config.ParticipantId, nameof(config.ParticipantId));
        NatsToken.EnsureSingleToken(config.OwnNetId, nameof(config.OwnNetId));
        if (config.ParentNetId is not null)
        {
            NatsToken.EnsureSingleToken(config.ParentNetId, nameof(config.ParentNetId));
        }

        // HostId becomes the agent.<hostId>.cmd subject token when present, so it must be a single token too.
        if (config.HostId is not null)
        {
            NatsToken.EnsureSingleToken(config.HostId, nameof(config.HostId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(config.DisplayName);
    }
}
