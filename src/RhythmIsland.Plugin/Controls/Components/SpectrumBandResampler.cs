namespace RhythmIsland.Controls.Components;

internal static class SpectrumBandResampler
{
    internal static IReadOnlyList<float> Resample(IReadOnlyList<float> source, int targetCount, double amplitude = 1.0)
    {
        if (source.Count == 0 || targetCount <= 0) return [];
        var scale = double.IsFinite(amplitude) ? Math.Clamp(amplitude, 0.25, 3.0) : 1.0;
        if (source.Count == targetCount && Math.Abs(scale - 1.0) < double.Epsilon) return source;

        var result = new float[targetCount];
        for (var target = 0; target < targetCount; target++)
        {
            var start = (int)Math.Floor(target * source.Count / (double)targetCount);
            var end = (int)Math.Ceiling((target + 1d) * source.Count / targetCount);
            var maximum = 0f;
            for (var sourceIndex = start; sourceIndex < Math.Min(end, source.Count); sourceIndex++)
            {
                var value = float.IsFinite(source[sourceIndex]) ? Math.Clamp(source[sourceIndex], 0f, 1f) : 0f;
                maximum = Math.Max(maximum, value);
            }
            result[target] = (float)Math.Clamp(maximum * scale, 0, 1);
        }
        return result;
    }
}
