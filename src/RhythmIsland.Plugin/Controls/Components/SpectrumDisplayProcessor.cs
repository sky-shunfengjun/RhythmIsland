using RhythmIsland.Models;
using RhythmIsland.Services;

namespace RhythmIsland.Controls.Components;

internal static class SpectrumDisplayProcessor
{
    private const double BalancedMaximumGainDb = 6.0;
    private const double HighBoostMaximumGainDb = 10.0;

    internal static void ProcessInto(
        IReadOnlyList<float> source,
        Span<float> destination,
        Span<float> resampleBuffer,
        SpectrumFrequencyBalanceMode balanceMode,
        double amplitude,
        bool horizontalMirrorEnabled)
    {
        if (destination.IsEmpty) return;
        if (source.Count == 0)
        {
            destination.Clear();
            return;
        }

        var logicalCount = horizontalMirrorEnabled ? destination.Length / 2 : destination.Length;
        if (logicalCount <= 0 || resampleBuffer.Length < logicalCount)
        {
            destination.Clear();
            return;
        }

        var logicalBands = resampleBuffer[..logicalCount];
        SpectrumBandResampler.ResampleInto(source, logicalBands);
        var scale = NormalizeAmplitude(amplitude);
        for (var index = 0; index < logicalCount; index++)
        {
            var balanced = ApplyFrequencyBalance(logicalBands[index], index, logicalCount, balanceMode);
            logicalBands[index] = (float)Math.Clamp(balanced * scale, 0, 1);
        }

        if (!horizontalMirrorEnabled)
        {
            logicalBands.CopyTo(destination);
            return;
        }

        for (var index = 0; index < logicalCount; index++)
        {
            var value = logicalBands[index];
            destination[logicalCount - 1 - index] = value;
            destination[logicalCount + index] = value;
        }
    }

    internal static bool HasVisibleSignal(
        IReadOnlyList<float> source,
        int detailCount,
        SpectrumFrequencyBalanceMode balanceMode,
        double amplitude,
        bool horizontalMirrorEnabled)
    {
        var logicalCount = horizontalMirrorEnabled ? detailCount / 2 : detailCount;
        if (source.Count == 0 || logicalCount <= 0 || logicalCount > 96) return false;

        Span<float> resampled = stackalloc float[logicalCount];
        SpectrumBandResampler.ResampleInto(source, resampled);
        var scale = NormalizeAmplitude(amplitude);
        for (var index = 0; index < logicalCount; index++)
        {
            var balanced = ApplyFrequencyBalance(resampled[index], index, logicalCount, balanceMode);
            if (balanced * scale >= SpectrumAnalyzer.SilenceFloor) return true;
        }

        return false;
    }

    internal static float ApplyFrequencyBalance(
        float value,
        int bandIndex,
        int bandCount,
        SpectrumFrequencyBalanceMode mode)
    {
        if (!float.IsFinite(value)) return 0;
        value = Math.Clamp(value, 0, 1);
        if (value < SpectrumAnalyzer.SilenceFloor) return 0;
        if (mode == SpectrumFrequencyBalanceMode.Original) return value;

        var validatedMode = Enum.IsDefined(mode) ? mode : SpectrumFrequencyBalanceMode.Balanced;
        var maximumGainDb = validatedMode == SpectrumFrequencyBalanceMode.HighBoost
            ? HighBoostMaximumGainDb
            : BalancedMaximumGainDb;
        var position = bandCount <= 1 ? 0 : Math.Clamp(bandIndex / (double)(bandCount - 1), 0, 1);
        var smoothPosition = position * position * (3 - 2 * position);
        var gain = Math.Pow(10, maximumGainDb * smoothPosition / 20);
        var balanced = value * gain / (1 + value * (gain - 1));
        return float.IsFinite((float)balanced) ? (float)Math.Clamp(balanced, 0, 1) : 0;
    }

    private static double NormalizeAmplitude(double amplitude) =>
        double.IsFinite(amplitude) ? Math.Clamp(amplitude, 0.25, 3.0) : 1.0;
}
