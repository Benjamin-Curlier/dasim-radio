namespace Dasim.Radio.MediaService.Degrade;

/// <summary>
/// How much a listener's reception is degraded: <see cref="QualityPercent"/> drives the re-encode
/// (bitrate + complexity) and <see cref="ClarityPercent"/> drives the PCM clarity DSP (band-limit +
/// noise). Both are 0–100; 100 means "no degradation" on that axis. A profile that is clean on both
/// axes (<see cref="IsClean"/>) means the listener hears the original stream untouched (pass-through).
/// </summary>
public readonly record struct DegradeProfile(int QualityPercent, int ClarityPercent)
{
    /// <summary>Builds a profile, clamping each axis into 0–100.</summary>
    public static DegradeProfile From(int qualityPercent, int clarityPercent) =>
        new(Math.Clamp(qualityPercent, 0, 100), Math.Clamp(clarityPercent, 0, 100));

    /// <summary>No degradation on either axis — the listener should receive the source unchanged.</summary>
    public bool IsClean => QualityPercent >= 100 && ClarityPercent >= 100;
}
