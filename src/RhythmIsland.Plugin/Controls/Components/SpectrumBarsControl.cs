using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RhythmIsland.Abstractions;
using RhythmIsland.Models;
using RhythmIsland.Services;

namespace RhythmIsland.Controls.Components;

public sealed class SpectrumBarsControl : Control
{
    private const double CurveThickness = 2.0;
    private const double FillOpacity = 0.35;
    private static readonly IBrush DefaultAccentBrush = new SolidColorBrush(Color.Parse("#FF4DA3FF"));

    public static readonly StyledProperty<IBrush?> BarBrushProperty =
        AvaloniaProperty.Register<SpectrumBarsControl, IBrush?>(nameof(BarBrush));

    private ISpectrumFrameProvider? _frames;
    private SpectrumVisualSettings? _visualSettings;
    private BrushCacheKey? _brushCacheKey;
    private IBrush? _cachedBrush;
    private PaletteCacheKey? _paletteCacheKey;
    private IReadOnlyList<IBrush> _cachedBarPalette = [];
    private LaserBrushCacheKey? _laserBrushCacheKey;
    private IBrush? _cachedLaserHighlightBrush;
    private LaserPaletteCacheKey? _laserPaletteCacheKey;
    private IReadOnlyList<IBrush> _cachedLaserHighlightPalette = [];
    private PenCacheKey? _penCacheKey;
    private Pen? _cachedCurvePen;
    private Pen? _cachedCurveHighlightPen;
    private Pen[] _cachedCurveHaloPens = [];
    private readonly double[] _curveHaloOpacities = new double[3];
    private readonly SpectrumFrameInterpolator _frameInterpolator = new();
    private readonly SpectrumPaletteTransition _paletteTransition = new();
    private readonly SpectrumCurveGeometryCache _curveGeometryCache = new();
    private Rect[] _barRectangles = [];
    private TimeProvider _timeProvider = TimeProvider.System;
    private DateTimeOffset _animationEpoch;
    private SpectrumPalette? _mediaPalette;
    private MediaPaletteCacheKey? _mediaPaletteCacheKey;
    private SpectrumPalette? _cachedProcessedMediaPalette;
    private DirectPaletteCacheKey? _directPaletteCacheKey;
    private SpectrumPalette? _cachedDirectPalette;
    private SpectrumPalette? _lastResolvedPalette;
    private int _effectiveFrameRate = 30;

    public SpectrumBarsControl() => _animationEpoch = _timeProvider.GetUtcNow();

    internal int ResampleBufferCapacity => _frameInterpolator.BufferCapacity;
    internal int BarRectangleBufferCapacity => _barRectangles.Length;
    internal int CurvePointBufferCapacity => _curveGeometryCache.PointCapacity;
    internal int CurveGeometryGeneration => _curveGeometryCache.GeometryGeneration;
    internal Geometry CurveUpperGeometry => _curveGeometryCache.UpperGeometry;
    internal Geometry CurveLowerGeometry => _curveGeometryCache.LowerGeometry;
    internal Geometry CurveBottomFillGeometry => _curveGeometryCache.BottomFillGeometry;
    internal Geometry CurveCenteredFillGeometry => _curveGeometryCache.CenteredFillGeometry;
    internal SpectrumPalette? LastResolvedPalette => _lastResolvedPalette;
    internal IBrush? CachedBrush => _cachedBrush;
    internal IBrush? CachedLaserHighlightBrush => _cachedLaserHighlightBrush;
    internal Pen? CachedCurvePen => _cachedCurvePen;
    internal IReadOnlyList<IBrush> CachedBarPalette => _cachedBarPalette;
    internal IReadOnlyList<IBrush> CachedLaserHighlightPalette => _cachedLaserHighlightPalette;
    internal int BarPaletteUpdateCount { get; private set; }
    internal int LaserPaletteUpdateCount { get; private set; }

    public IBrush? BarBrush
    {
        get => GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    internal void Initialize(ISpectrumFrameProvider frames) => _frames = frames;

    internal void SetComponentSettings(SpectrumComponentSettings settings) => SetVisualSettings(settings);

    internal void SetVisualSettings(SpectrumVisualSettings settings) => _visualSettings = settings;

    internal void SetMediaPalette(SpectrumPalette? palette) => _mediaPalette = palette;

    internal void SetEffectiveFrameRate(int frameRate) => _effectiveFrameRate = frameRate;

    internal void SetTimeProvider(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        _animationEpoch = timeProvider.GetUtcNow();
        _paletteTransition.Reset();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var componentSettings = _visualSettings;
        if (componentSettings is null) return;

        var now = _timeProvider.GetUtcNow();
        var hostBrush = BarBrush ?? DefaultAccentBrush;
        var themeColor = hostBrush is ISolidColorBrush solidColorBrush
            ? solidColorBrush.Color
            : Color.Parse("#FF4DA3FF");
        var targetPalette = ResolveTargetPalette(componentSettings, themeColor);
        var paletteTransition = _paletteTransition.Resolve(targetPalette, now);
        var baseColor = paletteTransition.Primary;
        var gradientEndColor = paletteTransition.Secondary;
        _lastResolvedPalette = paletteTransition;
        var dynamicPhase = componentSettings.GradientMode == SpectrumGradientMode.Dynamic
            ? SpectrumColorHelper.DynamicGradientPhase(componentSettings.GradientSpeed, now - _animationEpoch)
            : 0;
        var brush = ResolveBrush(componentSettings, baseColor, gradientEndColor, dynamicPhase);

        var frame = _frames?.Latest;
        if (frame is null || frame.IsSilent || frame.Bands.Count == 0)
        {
            _frameInterpolator.Reset();
            return;
        }

        var opacity = Math.Clamp(componentSettings.Opacity, 0.1, 1.0);
        var bands = _frameInterpolator.Resolve(frame, componentSettings.BarCount,
            componentSettings.Amplitude, _effectiveFrameRate, now,
            componentSettings.FrequencyBalanceMode, componentSettings.HorizontalMirrorEnabled);
        if (!SpectrumBandResampler.HasVisibleSignal(bands)) return;

        var padding = ResolveSafetyPadding(componentSettings.DisplayMode);
        var laserGlow = componentSettings.GlowEnabled
            ? SpectrumVisualEffectHelper.CalculateLaserGlow(componentSettings.GlowIntensity)
            : (SpectrumLaserGlowParameters?)null;
        var laserHighlightBrush = laserGlow is { } laser
            ? ResolveLaserHighlightBrush(componentSettings, baseColor, gradientEndColor,
                laser.HighlightLightenAmount, dynamicPhase)
            : null;

        using (context.PushOpacity(opacity))
        {
            switch (componentSettings.VisualizationStyle)
            {
                case SpectrumVisualizationStyle.SmoothLine:
                    RenderSmoothLine(context, brush, bands, componentSettings.DisplayMode, padding,
                        laserGlow, laserHighlightBrush);
                    break;
                case SpectrumVisualizationStyle.FilledCurve:
                    RenderFilledCurve(context, brush, bands, componentSettings.DisplayMode, padding,
                        laserGlow, laserHighlightBrush);
                    break;
                default:
                    var palette = componentSettings.GradientMode != SpectrumGradientMode.Off
                        ? ResolveBarGradientPalette(baseColor, gradientEndColor, bands.Count,
                            componentSettings.GradientMode, dynamicPhase)
                        : null;
                    var highlightPalette = componentSettings.GradientMode != SpectrumGradientMode.Off && laserGlow is { } barLaser
                        ? ResolveLaserHighlightPalette(baseColor, gradientEndColor, bands.Count,
                            barLaser.HighlightLightenAmount, componentSettings.GradientMode, dynamicPhase)
                        : null;
                    RenderBars(context, brush, palette, laserHighlightBrush, highlightPalette, bands,
                        componentSettings.DisplayMode, padding, laserGlow);
                    break;
            }
        }
    }

    internal static Thickness ResolveSafetyPadding(SpectrumDisplayMode mode) =>
        mode == SpectrumDisplayMode.BottomUp ? new Thickness(1, 1, 1, 3) : default;

    private SpectrumPalette ResolveTargetPalette(SpectrumVisualSettings settings, Color themeColor)
    {
        if (settings.ColorSource == SpectrumColorSource.MediaCover && _mediaPalette is { } mediaPalette)
        {
            var key = new MediaPaletteCacheKey(mediaPalette, settings.MediaCoverColorMode, themeColor);
            if (_mediaPaletteCacheKey != key || _cachedProcessedMediaPalette is null)
            {
                _cachedProcessedMediaPalette = SpectrumMediaPaletteProcessor.Process(
                    mediaPalette, settings.MediaCoverColorMode, themeColor);
                _mediaPaletteCacheKey = key;
            }
            return _cachedProcessedMediaPalette;
        }

        var primary = settings.ColorSource == SpectrumColorSource.Custom ? settings.CustomColor : themeColor;
        var secondary = settings.UseCustomGradientEndColor
            ? settings.GradientEndColor
            : SpectrumColorHelper.CreateAutomaticGradientEnd(primary);
        var directKey = new DirectPaletteCacheKey(primary, secondary);
        if (_directPaletteCacheKey != directKey || _cachedDirectPalette is null)
        {
            _directPaletteCacheKey = directKey;
            _cachedDirectPalette = new SpectrumPalette(primary, secondary);
        }
        return _cachedDirectPalette;
    }

    private IBrush ResolveBrush(
        SpectrumVisualSettings settings,
        Color baseColor,
        Color gradientEndColor,
        double dynamicPhase)
    {
        var key = new BrushCacheKey(settings.GradientMode);
        if (_brushCacheKey == key && _cachedBrush is not null)
        {
            UpdateBrush(_cachedBrush, settings.GradientMode, baseColor, gradientEndColor, dynamicPhase);
            return _cachedBrush;
        }

        _cachedBrush = settings.GradientMode switch
        {
            SpectrumGradientMode.Dynamic => CreateDynamicGradient(baseColor, gradientEndColor, dynamicPhase),
            SpectrumGradientMode.Static => SpectrumVisualEffectHelper.CreateHorizontalGradient(baseColor, gradientEndColor),
            _ => new SolidColorBrush(baseColor)
        };
        _brushCacheKey = key;
        return _cachedBrush;
    }

    private IReadOnlyList<IBrush> ResolveBarGradientPalette(
        Color startColor,
        Color endColor,
        int count,
        SpectrumGradientMode mode,
        double dynamicPhase)
    {
        var key = new PaletteCacheKey(count, mode);
        if (_paletteCacheKey == key && _cachedBarPalette.Count == count)
        {
            UpdatePalette(_cachedBarPalette, startColor, endColor, mode, dynamicPhase, 0);
            BarPaletteUpdateCount++;
            return _cachedBarPalette;
        }

        var brushes = new IBrush[count];
        for (var index = 0; index < count; index++)
        {
            var amount = count == 1 ? 0 : index / (double)(count - 1);
            var color = mode == SpectrumGradientMode.Dynamic
                ? SpectrumColorHelper.SampleDynamicGradient(startColor, endColor, amount, dynamicPhase)
                : SpectrumColorHelper.Interpolate(startColor, endColor, amount);
            brushes[index] = new SolidColorBrush(color);
        }
        _cachedBarPalette = brushes;
        _paletteCacheKey = key;
        BarPaletteUpdateCount++;
        return _cachedBarPalette;
    }

    private IBrush ResolveLaserHighlightBrush(
        SpectrumVisualSettings settings,
        Color startColor,
        Color endColor,
        double lightenAmount,
        double dynamicPhase)
    {
        var key = new LaserBrushCacheKey(settings.GradientMode);
        if (_laserBrushCacheKey == key && _cachedLaserHighlightBrush is not null)
        {
            UpdateBrush(
                _cachedLaserHighlightBrush,
                settings.GradientMode,
                SpectrumColorHelper.Lighten(startColor, lightenAmount),
                SpectrumColorHelper.Lighten(endColor, lightenAmount),
                dynamicPhase);
            return _cachedLaserHighlightBrush;
        }

        var lightStart = SpectrumColorHelper.Lighten(startColor, lightenAmount);
        var lightEnd = SpectrumColorHelper.Lighten(endColor, lightenAmount);
        _cachedLaserHighlightBrush = settings.GradientMode switch
        {
            SpectrumGradientMode.Dynamic => CreateDynamicGradient(lightStart, lightEnd, dynamicPhase),
            SpectrumGradientMode.Static => SpectrumVisualEffectHelper.CreateHorizontalGradient(lightStart, lightEnd),
            _ => new SolidColorBrush(lightStart)
        };
        _laserBrushCacheKey = key;
        return _cachedLaserHighlightBrush;
    }

    private IReadOnlyList<IBrush> ResolveLaserHighlightPalette(
        Color startColor,
        Color endColor,
        int count,
        double lightenAmount,
        SpectrumGradientMode mode,
        double dynamicPhase)
    {
        var key = new LaserPaletteCacheKey(count, mode);
        if (_laserPaletteCacheKey == key && _cachedLaserHighlightPalette.Count == count)
        {
            UpdatePalette(
                _cachedLaserHighlightPalette, startColor, endColor, mode, dynamicPhase, lightenAmount);
            LaserPaletteUpdateCount++;
            return _cachedLaserHighlightPalette;
        }

        var brushes = new IBrush[count];
        for (var index = 0; index < count; index++)
        {
            var amount = count == 1 ? 0 : index / (double)(count - 1);
            var color = mode == SpectrumGradientMode.Dynamic
                ? SpectrumColorHelper.SampleDynamicGradient(startColor, endColor, amount, dynamicPhase)
                : SpectrumColorHelper.Interpolate(startColor, endColor, amount);
            brushes[index] = new SolidColorBrush(SpectrumColorHelper.Lighten(color, lightenAmount));
        }

        _cachedLaserHighlightPalette = brushes;
        _laserPaletteCacheKey = key;
        LaserPaletteUpdateCount++;
        return _cachedLaserHighlightPalette;
    }

    private static void UpdateBrush(
        IBrush brush,
        SpectrumGradientMode mode,
        Color startColor,
        Color endColor,
        double dynamicPhase)
    {
        if (brush is SolidColorBrush solid)
        {
            solid.Color = startColor;
            return;
        }

        if (brush is not LinearGradientBrush gradient) return;
        if (mode == SpectrumGradientMode.Dynamic)
        {
            UpdateDynamicGradient(gradient, startColor, endColor, dynamicPhase);
            return;
        }

        if (gradient.GradientStops.Count >= 2)
        {
            gradient.GradientStops[0].Color = startColor;
            gradient.GradientStops[^1].Color = endColor;
        }
    }

    private static LinearGradientBrush CreateDynamicGradient(Color startColor, Color endColor, double phase)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative)
        };
        const int stopCount = 7;
        for (var index = 0; index < stopCount; index++)
        {
            var offset = index / (double)(stopCount - 1);
            brush.GradientStops.Add(new GradientStop(
                SpectrumColorHelper.SampleDynamicGradient(startColor, endColor, offset, phase), offset));
        }
        return brush;
    }

    private static void UpdateDynamicGradient(
        LinearGradientBrush brush,
        Color startColor,
        Color endColor,
        double phase)
    {
        for (var index = 0; index < brush.GradientStops.Count; index++)
        {
            var stop = brush.GradientStops[index];
            stop.Color = SpectrumColorHelper.SampleDynamicGradient(startColor, endColor, stop.Offset, phase);
        }
    }

    private static void UpdatePalette(
        IReadOnlyList<IBrush> palette,
        Color startColor,
        Color endColor,
        SpectrumGradientMode mode,
        double phase,
        double lightenAmount)
    {
        for (var index = 0; index < palette.Count; index++)
        {
            if (palette[index] is not SolidColorBrush brush) continue;
            var amount = palette.Count == 1 ? 0 : index / (double)(palette.Count - 1);
            var color = mode == SpectrumGradientMode.Dynamic
                ? SpectrumColorHelper.SampleDynamicGradient(startColor, endColor, amount, phase)
                : SpectrumColorHelper.Interpolate(startColor, endColor, amount);
            brush.Color = lightenAmount > 0 ? SpectrumColorHelper.Lighten(color, lightenAmount) : color;
        }
    }

    private void RenderBars(
        DrawingContext context,
        IBrush brush,
        IReadOnlyList<IBrush>? palette,
        IBrush? laserHighlightBrush,
        IReadOnlyList<IBrush>? laserHighlightPalette,
        IReadOnlyList<float> bands,
        SpectrumDisplayMode mode,
        Thickness padding,
        SpectrumLaserGlowParameters? laserGlow)
    {
        if (_barRectangles.Length != bands.Count) _barRectangles = new Rect[bands.Count];
        SpectrumBarLayout.CalculateInto(Bounds.Size, bands, mode, padding, _barRectangles);
        var rectangles = _barRectangles;
        if (laserGlow is { } laser && laserHighlightBrush is not null)
        {
            using var clip = context.PushClip(new Rect(
                padding.Left,
                padding.Top,
                Math.Max(0, Bounds.Width - padding.Left - padding.Right),
                Math.Max(0, Bounds.Height - padding.Top - padding.Bottom)));

            DrawBarHaloLayer(context, brush, palette, rectangles, laser.OuterSpread, laser.OuterOpacity);
            DrawBarHaloLayer(context, brush, palette, rectangles, laser.MiddleSpread, laser.MiddleOpacity);
            DrawBarHaloLayer(context, brush, palette, rectangles, laser.InnerSpread, laser.InnerOpacity);
            DrawLaserBarBodies(context, brush, palette, laserHighlightBrush, laserHighlightPalette,
                rectangles, laser);
            return;
        }

        for (var index = 0; index < rectangles.Length; index++)
        {
            var rectangle = rectangles[index];
            if (rectangle.Width <= 0 || rectangle.Height <= 0) continue;
            var radius = Math.Min(2.0, rectangle.Width / 2);
            var barBrush = palette is not null && index < palette.Count ? palette[index] : brush;
            context.DrawRectangle(barBrush, null, rectangle, radius, radius);
        }
    }

    private void RenderSmoothLine(
        DrawingContext context,
        IBrush brush,
        IReadOnlyList<float> bands,
        SpectrumDisplayMode mode,
        Thickness padding,
        SpectrumLaserGlowParameters? laserGlow,
        IBrush? laserHighlightBrush)
    {
        if (!_curveGeometryCache.Update(Bounds.Size, bands, mode, padding, includeFill: false)) return;

        var upper = _curveGeometryCache.UpperGeometry;
        var lower = mode == SpectrumDisplayMode.Centered
            ? _curveGeometryCache.LowerGeometry
            : null;
        if (laserGlow is { } laser && laserHighlightBrush is not null)
        {
            using var clip = context.PushClip(_curveGeometryCache.DrawingBounds);
            DrawLaserCurve(context, brush, laserHighlightBrush, upper, laser);
            if (lower is not null) DrawLaserCurve(context, brush, laserHighlightBrush, lower, laser);
            return;
        }

        var pen = ResolveCurvePens(brush, null, null).Body;
        context.DrawGeometry(null, pen, upper);
        if (mode == SpectrumDisplayMode.Centered)
            context.DrawGeometry(null, pen, lower!);
    }

    private void RenderFilledCurve(
        DrawingContext context,
        IBrush brush,
        IReadOnlyList<float> bands,
        SpectrumDisplayMode mode,
        Thickness padding,
        SpectrumLaserGlowParameters? laserGlow,
        IBrush? laserHighlightBrush)
    {
        if (!_curveGeometryCache.Update(Bounds.Size, bands, mode, padding, includeFill: true)) return;

        var fill = mode == SpectrumDisplayMode.Centered
            ? _curveGeometryCache.CenteredFillGeometry
            : _curveGeometryCache.BottomFillGeometry;
        using (context.PushOpacity(FillOpacity))
            context.DrawGeometry(brush, null, fill);

        var upper = _curveGeometryCache.UpperGeometry;
        var lower = mode == SpectrumDisplayMode.Centered
            ? _curveGeometryCache.LowerGeometry
            : null;
        if (laserGlow is { } laser && laserHighlightBrush is not null)
        {
            using var clip = context.PushClip(_curveGeometryCache.DrawingBounds);
            DrawLaserCurve(context, brush, laserHighlightBrush, upper, laser);
            if (lower is not null) DrawLaserCurve(context, brush, laserHighlightBrush, lower, laser);
            return;
        }

        var pen = ResolveCurvePens(brush, null, null).Body;
        context.DrawGeometry(null, pen, upper);
        if (mode == SpectrumDisplayMode.Centered)
            context.DrawGeometry(null, pen, lower!);
    }

    private static void DrawBarHaloLayer(
        DrawingContext context,
        IBrush fallbackBrush,
        IReadOnlyList<IBrush>? palette,
        IReadOnlyList<Rect> rectangles,
        double spread,
        double opacity)
    {
        using (context.PushOpacity(opacity))
        {
            for (var index = 0; index < rectangles.Count; index++)
            {
                var rectangle = rectangles[index];
                if (rectangle.Width <= 0 || rectangle.Height <= 0) continue;
                var glowBrush = palette is not null && index < palette.Count ? palette[index] : fallbackBrush;
                var horizontalSpread = spread * 0.55;
                var expanded = new Rect(
                    rectangle.X - horizontalSpread,
                    rectangle.Y - spread,
                    rectangle.Width + horizontalSpread * 2,
                    rectangle.Height + spread * 2);
                var radius = Math.Min(2.0 + spread, expanded.Width / 2);
                context.DrawRectangle(glowBrush, null, expanded, radius, radius);
            }
        }
    }

    private static void DrawLaserBarBodies(
        DrawingContext context,
        IBrush fallbackBrush,
        IReadOnlyList<IBrush>? palette,
        IBrush fallbackHighlightBrush,
        IReadOnlyList<IBrush>? highlightPalette,
        IReadOnlyList<Rect> rectangles,
        SpectrumLaserGlowParameters laser)
    {
        for (var index = 0; index < rectangles.Count; index++)
        {
            var rectangle = rectangles[index];
            if (rectangle.Width <= 0 || rectangle.Height <= 0) continue;

            var bodyBrush = palette is not null && index < palette.Count ? palette[index] : fallbackBrush;
            var highlightBrush = highlightPalette is not null && index < highlightPalette.Count
                ? highlightPalette[index]
                : fallbackHighlightBrush;
            var radius = Math.Min(2.0, rectangle.Width / 2);
            context.DrawRectangle(bodyBrush, null, rectangle, radius, radius);

            var highlight = SpectrumVisualEffectHelper.CalculateBarHighlight(rectangle, laser);
            using (context.PushOpacity(laser.HighlightOpacity))
                context.DrawRectangle(highlightBrush, null, highlight,
                    Math.Min(2.0, highlight.Width / 2),
                    Math.Min(2.0, highlight.Width / 2));
        }
    }

    private void DrawLaserCurve(
        DrawingContext context,
        IBrush brush,
        IBrush highlightBrush,
        Geometry geometry,
        SpectrumLaserGlowParameters laser)
    {
        var pens = ResolveCurvePens(brush, highlightBrush, laser);
        _curveHaloOpacities[0] = laser.OuterOpacity;
        _curveHaloOpacities[1] = laser.MiddleOpacity;
        _curveHaloOpacities[2] = laser.InnerOpacity;
        for (var index = 0; index < pens.Halos.Count; index++)
        {
            using (context.PushOpacity(_curveHaloOpacities[index]))
                context.DrawGeometry(null, pens.Halos[index], geometry);
        }
        context.DrawGeometry(null, pens.Body, geometry);
        using (context.PushOpacity(laser.HighlightOpacity))
            context.DrawGeometry(null, pens.Highlight, geometry);
    }

    private (Pen Body, Pen Highlight, IReadOnlyList<Pen> Halos) ResolveCurvePens(
        IBrush bodyBrush,
        IBrush? highlightBrush,
        SpectrumLaserGlowParameters? laser)
    {
        var key = new PenCacheKey(
            bodyBrush,
            highlightBrush,
            laser is not null,
            laser?.OuterSpread ?? 0,
            laser?.MiddleSpread ?? 0,
            laser?.InnerSpread ?? 0);
        if (_penCacheKey != key || _cachedCurvePen is null)
        {
            _cachedCurvePen = new Pen(bodyBrush, CurveThickness);
            _cachedCurveHighlightPen = new Pen(highlightBrush ?? bodyBrush, 1.5);
            _cachedCurveHaloPens = laser is { } glow
                ? [
                    new Pen(bodyBrush, CurveThickness + glow.OuterSpread * 2),
                    new Pen(bodyBrush, CurveThickness + glow.MiddleSpread * 2),
                    new Pen(bodyBrush, CurveThickness + glow.InnerSpread * 2)
                ]
                : [];
            _penCacheKey = key;
        }

        return (_cachedCurvePen, _cachedCurveHighlightPen!, _cachedCurveHaloPens);
    }

    private readonly record struct BrushCacheKey(SpectrumGradientMode GradientMode);
    private readonly record struct PaletteCacheKey(
        int Count,
        SpectrumGradientMode GradientMode);
    private readonly record struct LaserBrushCacheKey(SpectrumGradientMode GradientMode);
    private readonly record struct LaserPaletteCacheKey(
        int Count,
        SpectrumGradientMode GradientMode);
    private readonly record struct PenCacheKey(
        IBrush BodyBrush,
        IBrush? HighlightBrush,
        bool HasLaser,
        double OuterSpread,
        double MiddleSpread,
        double InnerSpread);
    private readonly record struct MediaPaletteCacheKey(
        SpectrumPalette Palette,
        SpectrumMediaColorMode Mode,
        Color ThemeColor);
    private readonly record struct DirectPaletteCacheKey(Color Primary, Color Secondary);
}
