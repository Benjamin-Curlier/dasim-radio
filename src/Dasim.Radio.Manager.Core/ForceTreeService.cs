using Dasim.Radio.Contracts;
using Dasim.Radio.Messaging.KeyValue;
using Microsoft.Extensions.Logging;

namespace Dasim.Radio.Manager.Core;

/// <summary>The current force tree together with its KV revision.</summary>
public sealed record ForceTreeImport(ForceTreeDto Tree, ulong Revision);

/// <summary>Thrown when a force tree fails validation on import.</summary>
public sealed class ForceTreeValidationException(IReadOnlyList<string> errors)
    : Exception("The force tree is invalid: " + string.Join("; ", errors))
{
    /// <summary>The individual validation errors.</summary>
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>Reads and imports the authoritative force tree (<c>force_tree</c> bucket, key <c>current</c>).</summary>
public interface IForceTreeService
{
    /// <summary>Reads the current force tree with its revision, or <c>null</c> if none is stored.</summary>
    ValueTask<ForceTreeImport?> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>Validates and stores <paramref name="tree"/> as the current force tree.</summary>
    /// <exception cref="ForceTreeValidationException">The tree is structurally invalid.</exception>
    ValueTask ImportAsync(ForceTreeDto tree, CancellationToken cancellationToken = default);
}

/// <summary>JetStream-KV implementation of <see cref="IForceTreeService"/>.</summary>
public sealed class ForceTreeService(IControlPlaneStore store, ILogger<ForceTreeService> logger) : IForceTreeService
{
    private readonly IControlPlaneStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ILogger<ForceTreeService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async ValueTask<ForceTreeImport?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        INatsKeyValueStore<ForceTreeDto> bucket = await _store.ForceTreeAsync(cancellationToken).ConfigureAwait(false);
        KeyValueEntry<ForceTreeDto>? entry =
            await bucket.TryGetAsync(Subjects.Keys.ForceTreeCurrent, cancellationToken).ConfigureAwait(false);
        return entry is { } found ? new ForceTreeImport(found.Value, found.Revision) : null;
    }

    public async ValueTask ImportAsync(ForceTreeDto tree, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);

        // Normalize first so a node that omitted its children (null after JSON deserialization) is stored as a
        // well-formed leaf — the media service's mapper dereferences Children and must never see null.
        ForceTreeDto normalized = ForceTreeValidator.Normalize(tree);

        IReadOnlyList<string> errors = ForceTreeValidator.Validate(normalized);
        if (errors.Count > 0)
        {
            throw new ForceTreeValidationException(errors);
        }

        INatsKeyValueStore<ForceTreeDto> bucket = await _store.ForceTreeAsync(cancellationToken).ConfigureAwait(false);
        await bucket.PutAsync(Subjects.Keys.ForceTreeCurrent, normalized, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Imported force tree version {Version}.", normalized.Version);
    }
}
