namespace RhythmIsland.Controls.Components;

using RhythmIsland.Services;

internal static class SpectrumBandResampler
{
    internal static IReadOnlyList<float> Resample(IReadOnlyList<float> source, int targetCount, double amplitude = 1.0)
    {
        if (source.Count == 0 || targetCount <= 0) return [];
        var scale = NormalizeAmplitude(amplitude);
        if (source.Count == targetCount && Math.Abs(scale - 1.0) < double.Epsilon) return source;

        var result = new float[targetCount];
        ResampleInto(source, result, amplitude);
        return result;
    }

    internal static void ResampleInto(IReadOnlyList<float> source, Span<float> destination, double amplitude = 1.0)
    {
        if (destination.IsEmpty) return;
        if (source.Count == 0)
        {
            destination.Clear();
            return;
        }
        var scale = NormalizeAmplitude(amplitude);
        for (var target = 0; target < destination.Length; target++)
        {
            var start = (int)Math.Floor(target * source.Count / (double)destination.Length);
            var end = (int)Math.Ceiling((target + 1d) * source.Count / destination.Length);
            var maximum = 0f;
            for (var sourceIndex = start; sourceIndex < Math.Min(end, source.Count); sourceIndex++)
            {
                var value = float.IsFinite(source[sourceIndex]) ? Math.Clamp(source[sourceIndex], 0f, 1f) : 0f;
                maximum = Math.Max(maximum, value);
            }
            destination[target] = (float)Math.Clamp(maximum * scale, 0, 1);
        }
    }

    internal static bool HasVisibleSignal(IReadOnlyList<float> bands)
    {
        foreach (var band in bands)
        {
            if (float.IsFinite(band) && band >= SpectrumAnalyzer.SilenceFloor)
                return true;
        }

        return false;
    }

    internal static bool HasVisibleSignal(IReadOnlyList<float> bands, double amplitude)
    {
        var scale = NormalizeAmplitude(amplitude);
        foreach (var band in bands)
        {
            var value = float.IsFinite(band) ? Math.Clamp(band, 0f, 1f) : 0f;
            if (value * scale >= SpectrumAnalyzer.SilenceFloor) return true;
        }

        return false;
    }

    private static double NormalizeAmplitude(double amplitude) =>
        double.IsFinite(amplitude) ? Math.Clamp(amplitude, 0.25, 3.0) : 1.0;
}
