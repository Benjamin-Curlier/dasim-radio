using System.Collections.Concurrent;
using Dasim.Radio.Contracts;
using Dasim.Radio.Core;

namespace Dasim.Radio.MediaService.Degrade;

/// <summary>
/// The current per-listener degradation profiles. Written by the <see cref="DegradeCommandService"/>
/// (off the <c>cmd.degrade</c> stream) and read by the mix renderer per frame, so it is thread-safe.
/// </summary>
/// <remarks>
/// v1 applies degradation to the whole listener; <see cref="DegradeCommand.NetId"/> (per-net scoping)
/// is accepted but not yet honoured — tracked for a later slice.
/// </remarks>
public interface IDegradeRegistry
{
    /// <summary>Applies a degrade command (a clean profile clears any existing degradation for that listener).</summary>
    void Apply(DegradeCommand command);

    /// <summary>Returns the active (non-clean) profile for a listener, if any.</summary>
    bool TryGetProfile(ParticipantId listener, out DegradeProfile profile);
}

/// <inheritdoc cref="IDegradeRegistry"/>
public sealed class DegradeRegistry : IDegradeRegistry
{
    private readonly ConcurrentDictionary<ParticipantId, DegradeProfile> _byListener = new();

    public void Apply(DegradeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var listener = new ParticipantId(command.TargetClientId);
        DegradeProfile profile = DegradeProfile.From(command.QualityPercent, command.ClarityPercent);

        if (profile.IsClean)
        {
            // "Restore": drop the entry so the listener falls back to pass-through.
            _byListener.TryRemove(listener, out _);
            return;
        }

        _byListener[listener] = profile;
    }

    public bool TryGetProfile(ParticipantId listener, out DegradeProfile profile) =>
        _byListener.TryGetValue(listener, out profile);
}
