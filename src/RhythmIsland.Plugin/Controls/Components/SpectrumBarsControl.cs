using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RhythmIsland.Abstractions;
using RhythmIsland.Models;
using RhythmIsland.Services;

namespace RhythmIsland.Controls.Components;

public sealed class SpectrumBarsControl : Control
{
    public static readonly StyledProperty<IBrush?> BarBrushProperty =
        AvaloniaProperty.Register<SpectrumBarsControl, IBrush?>(nameof(BarBrush));

    private ISpectrumFrameProvider? _frames;
    private SpectrumComponentSettings? _componentSettings;

    public IBrush? BarBrush
    {
        get => GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    internal void Initialize(ISpectrumFrameProvider frames) => _frames = frames;

    internal void SetComponentSettings(SpectrumComponentSettings settings) => _componentSettings = settings;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var frame = _frames?.Latest;
        var componentSettings = _componentSettings;
        if (frame is null || componentSettings is null || frame.IsSilent || frame.Bands.Count == 0) return;

        var brush = componentSettings.UseCustomColor
            ? new SolidColorBrush(componentSettings.CustomColor)
            : BarBrush ?? Brushes.White;
        var opacity = Math.Clamp(componentSettings.Opacity, 0.1, 1.0);
        var bands = SpectrumBandResampler.Resample(
            frame.Bands,
            componentSettings.BarCount,
            componentSettings.Amplitude);
        var padding = componentSettings.DisplayMode == SpectrumDisplayMode.BottomUp
            ? new Thickness(1, 1, 1, 3)
            : default;
        var rectangles = SpectrumBarLayout.Calculate(
            Bounds.Size,
            bands,
            componentSettings.DisplayMode,
            padding);

        using (context.PushOpacity(opacity))
        {
            foreach (var rectangle in rectangles)
            {
                if (rectangle.Width <= 0 || rectangle.Height <= 0) continue;
                var radius = Math.Min(2.0, rectangle.Width / 2);
                context.DrawRectangle(brush, null, rectangle, radius, radius);
            }
        }
    }
}
