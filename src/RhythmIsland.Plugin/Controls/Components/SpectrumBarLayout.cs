using Avalonia;
using RhythmIsland.Models;
using RhythmIsland.Services;

namespace RhythmIsland.Controls.Components;

internal static class SpectrumBarLayout
{
    internal static IReadOnlyList<Rect> Calculate(Size size, IReadOnlyList<float> bands,
        SpectrumDisplayMode mode, Thickness padding = default)
    {
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) ||
            size.Width <= 0 || size.Height <= 0 || bands.Count == 0)
            return [];

        var availableWidth = Math.Max(0, size.Width - padding.Left - padding.Right);
        var availableHeight = Math.Max(0, size.Height - padding.Top - padding.Bottom);
        if (availableWidth <= 0 || availableHeight <= 0) return [];

        var rectangles = new Rect[bands.Count];
        CalculateInto(size, bands, mode, padding, rectangles);
        return rectangles;
    }

    internal static void CalculateInto(Size size, IReadOnlyList<float> bands,
        SpectrumDisplayMode mode, Thickness padding, Span<Rect> rectangles)
    {
        if (rectangles.Length < bands.Count) throw new ArgumentException("柱体缓冲区长度不足。", nameof(rectangles));
        rectangles[..bands.Count].Clear();
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) ||
            size.Width <= 0 || size.Height <= 0 || bands.Count == 0)
            return;

        var availableWidth = Math.Max(0, size.Width - padding.Left - padding.Right);
        var availableHeight = Math.Max(0, size.Height - padding.Top - padding.Bottom);
        if (availableWidth <= 0 || availableHeight <= 0) return;

        var slotWidth = availableWidth / bands.Count;
        var barWidth = Math.Max(0, slotWidth * 0.72);

        for (var index = 0; index < bands.Count; index++)
        {
            var value = float.IsFinite(bands[index]) ? Math.Clamp(bands[index], 0f, 1f) : 0f;
            if (value < SpectrumAnalyzer.SilenceFloor) value = 0;
            var height = availableHeight * value;
            var x = Math.Clamp(padding.Left + index * slotWidth + (slotWidth - barWidth) / 2,
                padding.Left, size.Width - padding.Right);
            var width = Math.Clamp(barWidth, 0, size.Width - padding.Right - x);
            var y = mode == SpectrumDisplayMode.Centered
                ? padding.Top + (availableHeight - height) / 2
                : padding.Top + availableHeight - height;
            var top = Math.Clamp(y, padding.Top, size.Height - padding.Bottom);
            var bottom = Math.Clamp(y + height, padding.Top, size.Height - padding.Bottom);
            rectangles[index] = new Rect(x, top, width, Math.Max(0, bottom - top));
        }

    }
}
