using RhythmIsland.Models;

namespace RhythmIsland.Services;

internal static class SpectrumFrameRateOptions
{
    private static readonly int[] LimitedFrameRates = [30, 60, 90, 120];

    internal static IReadOnlyList<SpectrumFrameRateOption> ForRefreshRate(double? refreshRate)
    {
        var maximum = double.IsFinite(refreshRate ?? double.NaN) && refreshRate > 0
            ? Math.Max(30, refreshRate.Value + 0.5)
            : 60;
        var options = LimitedFrameRates
            .Where(frameRate => frameRate <= maximum)
            .Select(frameRate => new SpectrumFrameRateOption(frameRate, $"{frameRate} FPS"))
            .ToList();
        options.Add(new SpectrumFrameRateOption(0, "更高（不推荐）"));
        return options;
    }

    internal static int ResolveHigherFrameRate(double? refreshRate)
    {
        if (refreshRate is not { } value || !double.IsFinite(value) || value <= 0) return 60;
        return Math.Clamp((int)Math.Round(value), 60, 240);
    }
}
