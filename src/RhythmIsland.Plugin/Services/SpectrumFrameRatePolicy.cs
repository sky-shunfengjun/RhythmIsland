namespace RhythmIsland.Services;

internal static class SpectrumFrameRatePolicy
{
    internal static int ResolveEffectiveFrameRate(int configuredFrameRate, double? displayRefreshRate)
    {
        if (configuredFrameRate is 30 or 60) return configuredFrameRate;
        if (configuredFrameRate == 0)
            return SpectrumFrameRateOptions.ResolveHigherFrameRate(displayRefreshRate);

        if (configuredFrameRate is 90 or 120)
        {
            if (!IsReliable(displayRefreshRate)) return 60;
            return displayRefreshRate!.Value + 0.5 >= configuredFrameRate ? configuredFrameRate : 60;
        }

        return 30;
    }

    internal static int ResolvePersistedFrameRate(int configuredFrameRate, double? displayRefreshRate)
    {
        if (configuredFrameRate is not (90 or 120) || !IsReliable(displayRefreshRate))
            return configuredFrameRate;

        return displayRefreshRate!.Value + 0.5 >= configuredFrameRate ? configuredFrameRate : 60;
    }

    internal static bool IsReliable(double? refreshRate) =>
        refreshRate is { } value && double.IsFinite(value) && value > 1;
}
