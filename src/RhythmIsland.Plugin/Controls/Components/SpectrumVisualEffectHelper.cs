using Avalonia;
using Avalonia.Media;

namespace RhythmIsland.Controls.Components;

internal readonly record struct SpectrumLaserGlowParameters(
    double OuterSpread,
    double MiddleSpread,
    double InnerSpread,
    double OuterOpacity,
    double MiddleOpacity,
    double InnerOpacity,
    double HighlightWidthRatio,
    double HighlightLightenAmount,
    double HighlightOpacity);

internal static class SpectrumVisualEffectHelper
{
    internal static LinearGradientBrush CreateHorizontalGradient(Color startColor, Color endColor)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative)
        };
        brush.GradientStops.Add(new GradientStop(startColor, 0));
        brush.GradientStops.Add(new GradientStop(endColor, 1));
        return brush;
    }

    internal static SpectrumLaserGlowParameters CalculateLaserGlow(double intensity)
    {
        var value = double.IsFinite(intensity) ? Math.Clamp(intensity, 0.10, 1.00) : 0.50;
        return new SpectrumLaserGlowParameters(
            OuterSpread: 3 + 7 * value,
            MiddleSpread: 1.5 + 4.5 * value,
            InnerSpread: 0.5 + 1.5 * value,
            OuterOpacity: 0.03 + 0.07 * value,
            MiddleOpacity: 0.06 + 0.14 * value,
            InnerOpacity: 0.12 + 0.23 * value,
            HighlightWidthRatio: 0.70 + 0.10 * value,
            HighlightLightenAmount: 0.18 + 0.17 * value,
            HighlightOpacity: 0.70 + 0.25 * value);
    }

    internal static Rect CalculateBarHighlight(Rect rectangle, SpectrumLaserGlowParameters glow)
    {
        if (!double.IsFinite(rectangle.Width) || !double.IsFinite(rectangle.Height) ||
            rectangle.Width <= 0 || rectangle.Height <= 0)
            return default;

        var width = Math.Clamp(rectangle.Width * glow.HighlightWidthRatio, 0, rectangle.Width);
        return new Rect(
            rectangle.Center.X - width / 2,
            rectangle.Y,
            width,
            rectangle.Height);
    }
}
