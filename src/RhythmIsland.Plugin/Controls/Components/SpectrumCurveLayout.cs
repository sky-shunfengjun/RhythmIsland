using Avalonia;
using RhythmIsland.Models;
using RhythmIsland.Services;

namespace RhythmIsland.Controls.Components;

internal sealed record SpectrumCurvePoints(
    IReadOnlyList<Point> Upper,
    IReadOnlyList<Point> Lower,
    Rect DrawingBounds)
{
    internal bool IsEmpty => Upper.Count == 0;
}

internal static class SpectrumCurveLayout
{
    internal static SpectrumCurvePoints Calculate(
        Size size,
        IReadOnlyList<float> bands,
        SpectrumDisplayMode mode,
        Thickness padding = default)
    {
        if (bands.Count == 0) return new SpectrumCurvePoints([], [], default);
        var upper = new Point[bands.Count];
        var lower = mode == SpectrumDisplayMode.Centered ? new Point[bands.Count] : [];
        return CalculateInto(size, bands, mode, padding, upper, lower, out var bounds)
            ? new SpectrumCurvePoints(upper, lower, bounds)
            : new SpectrumCurvePoints([], [], default);
    }

    internal static bool CalculateInto(
        Size size,
        IReadOnlyList<float> bands,
        SpectrumDisplayMode mode,
        Thickness padding,
        Span<Point> upper,
        Span<Point> lower,
        out Rect bounds)
    {
        bounds = default;
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) ||
            size.Width <= 0 || size.Height <= 0 || bands.Count == 0 || upper.Length < bands.Count ||
            (mode == SpectrumDisplayMode.Centered && lower.Length < bands.Count))
            return false;

        var availableWidth = Math.Max(0, size.Width - padding.Left - padding.Right);
        var availableHeight = Math.Max(0, size.Height - padding.Top - padding.Bottom);
        if (availableWidth <= 0 || availableHeight <= 0) return false;

        bounds = new Rect(padding.Left, padding.Top, availableWidth, availableHeight);
        var centerY = bounds.Top + bounds.Height / 2;

        for (var index = 0; index < bands.Count; index++)
        {
            var value = float.IsFinite(bands[index]) ? Math.Clamp(bands[index], 0f, 1f) : 0f;
            if (value < SpectrumAnalyzer.SilenceFloor) value = 0;
            var x = bands.Count == 1
                ? bounds.Left + bounds.Width / 2
                : bounds.Left + bounds.Width * index / (bands.Count - 1);

            if (mode == SpectrumDisplayMode.Centered)
            {
                var halfHeight = bounds.Height * value / 2;
                upper[index] = new Point(x, Math.Clamp(centerY - halfHeight, bounds.Top, bounds.Bottom));
                lower[index] = new Point(x, Math.Clamp(centerY + halfHeight, bounds.Top, bounds.Bottom));
            }
            else
            {
                var y = bounds.Bottom - bounds.Height * value;
                upper[index] = new Point(x, Math.Clamp(y, bounds.Top, bounds.Bottom));
            }
        }

        return true;
    }
}
