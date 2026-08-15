using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using RhythmIsland.Controls.Components;
using RhythmIsland.Models;
using RhythmIsland.Services;
using Xunit.Abstractions;

namespace RhythmIsland.Tests;

public sealed class SpectrumRenderingTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;
    [AvaloniaTheory]
    [InlineData(SpectrumVisualizationStyle.Bars, SpectrumDisplayMode.BottomUp, 24)]
    [InlineData(SpectrumVisualizationStyle.Bars, SpectrumDisplayMode.Centered, 96)]
    [InlineData(SpectrumVisualizationStyle.SmoothLine, SpectrumDisplayMode.BottomUp, 48)]
    [InlineData(SpectrumVisualizationStyle.SmoothLine, SpectrumDisplayMode.Centered, 64)]
    [InlineData(SpectrumVisualizationStyle.FilledCurve, SpectrumDisplayMode.BottomUp, 96)]
    [InlineData(SpectrumVisualizationStyle.FilledCurve, SpectrumDisplayMode.Centered, 32)]
    public void StylesGradientAndGlowRenderOffscreenWithoutThrowing(
        SpectrumVisualizationStyle style,
        SpectrumDisplayMode mode,
        int detailCount)
    {
        using var bitmap = Render(style, mode, detailCount, new Size(300, 64), 1);
        Assert.Equal(new PixelSize(300, 64), bitmap.PixelSize);
    }

    [AvaloniaFact]
    public void EveryStyleDirectionAndDetailCountRendersWithGradientAndFiveLayerGlow()
    {
        var detailCounts = new[] { 24, 32, 48, 64, 96 };
        foreach (var style in Enum.GetValues<SpectrumVisualizationStyle>())
        foreach (var mode in Enum.GetValues<SpectrumDisplayMode>())
        foreach (var detailCount in detailCounts)
        {
            using var bitmap = Render(style, mode, detailCount, new Size(300, 64), 1);
            Assert.Equal(new PixelSize(300, 64), bitmap.PixelSize);
        }
    }

    [AvaloniaFact]
    public void EveryStyleDirectionAndFrequencyBalanceRendersWithHorizontalMirror()
    {
        foreach (var style in Enum.GetValues<SpectrumVisualizationStyle>())
        foreach (var mode in Enum.GetValues<SpectrumDisplayMode>())
        foreach (var balanceMode in Enum.GetValues<SpectrumFrequencyBalanceMode>())
        {
            var provider = new SpectrumFrameProvider();
            provider.Publish(CreateFrame(0));
            var control = CreateControl(provider, style, mode, 48, new Size(300, 64));
            control.SetComponentSettings(new SpectrumComponentSettings
            {
                VisualizationStyle = style,
                DisplayMode = mode,
                HorizontalMirrorEnabled = true,
                FrequencyBalanceMode = balanceMode,
                BarCount = 48,
                GradientMode = SpectrumGradientMode.Dynamic,
                GlowEnabled = true,
                GlowIntensity = 0.75
            });
            using var bitmap = new RenderTargetBitmap(new PixelSize(300, 64), new Vector(96, 96));

            bitmap.Render(control);
        }
    }

    [AvaloniaFact]
    public void BackgroundVisualSettingsRenderEveryStyleDirectionAndDetailCount()
    {
        foreach (var style in Enum.GetValues<SpectrumVisualizationStyle>())
        foreach (var mode in Enum.GetValues<SpectrumDisplayMode>())
        foreach (var detailCount in RhythmIslandSettings.AllowedBarCounts)
        {
            var provider = new SpectrumFrameProvider();
            provider.Publish(CreateFrame(0));
            var control = new SpectrumBarsControl
            {
                Width = 360,
                Height = 72,
                BarBrush = Brushes.MediumPurple
            };
            control.Initialize(provider);
            control.SetVisualSettings(new SpectrumBackgroundSettings
            {
                VisualizationStyle = style,
                DisplayMode = mode,
                BarCount = detailCount,
                HorizontalMirrorEnabled = true,
                FrequencyBalanceMode = SpectrumFrequencyBalanceMode.Balanced,
                GradientMode = SpectrumGradientMode.Dynamic,
                GlowEnabled = true,
                GlowIntensity = 0.75
            });
            control.Measure(new Size(360, 72));
            control.Arrange(new Rect(0, 0, 360, 72));
            using var bitmap = new RenderTargetBitmap(new PixelSize(360, 72), new Vector(96, 96));
            bitmap.Render(control);
        }
    }

    [AvaloniaFact]
    public void EveryStyleAndDirectionRendersDynamicCoverGradient()
    {
        foreach (var style in Enum.GetValues<SpectrumVisualizationStyle>())
        foreach (var mode in Enum.GetValues<SpectrumDisplayMode>())
        foreach (var colorMode in Enum.GetValues<SpectrumMediaColorMode>())
        {
            var provider = new SpectrumFrameProvider();
            provider.Publish(CreateFrame(0));
            var control = new SpectrumBarsControl
            {
                Width = 300,
                Height = 64,
                BarBrush = Brushes.DodgerBlue
            };
            control.Initialize(provider);
            control.SetMediaPalette(new SpectrumPalette(Colors.Magenta, Colors.Cyan));
            control.SetComponentSettings(new SpectrumComponentSettings
            {
                VisualizationStyle = style,
                DisplayMode = mode,
                ColorSource = SpectrumColorSource.MediaCover,
                MediaCoverColorMode = colorMode,
                GradientMode = SpectrumGradientMode.Dynamic,
                GradientSpeed = SpectrumGradientSpeed.Fast,
                GlowEnabled = true,
                GlowIntensity = 0.75
            });
            control.Measure(new Size(300, 64));
            control.Arrange(new Rect(0, 0, 300, 64));
            using var bitmap = new RenderTargetBitmap(new PixelSize(300, 64), new Vector(96, 96));

            bitmap.Render(control);
        }
    }

    [AvaloniaFact]
    public void ExtremelyNarrowGlowRenderDoesNotThrow()
    {
        using var bitmap = Render(
            SpectrumVisualizationStyle.Bars,
            SpectrumDisplayMode.BottomUp,
            96,
            new Size(1, 40),
            1);
        Assert.Equal(new PixelSize(1, 40), bitmap.PixelSize);
    }

    [AvaloniaFact]
    public void BottomUpRenderKeepsSafetyAreaAndCenteredKeepsItsExistingEdge()
    {
        using var bottomUp = Render(
            SpectrumVisualizationStyle.Bars,
            SpectrumDisplayMode.BottomUp,
            96,
            new Size(300, 40),
            1);
        using var centered = Render(
            SpectrumVisualizationStyle.Bars,
            SpectrumDisplayMode.Centered,
            96,
            new Size(300, 40),
            1);

        var bottomPadding = SpectrumBarsControl.ResolveSafetyPadding(SpectrumDisplayMode.BottomUp);
        var centeredPadding = SpectrumBarsControl.ResolveSafetyPadding(SpectrumDisplayMode.Centered);
        Assert.Equal(new Thickness(1, 1, 1, 3), bottomPadding);
        Assert.Equal(default, centeredPadding);

        var fullBands = Enumerable.Repeat(1f, 96).ToArray();
        var bottomRects = SpectrumBarLayout.Calculate(new Size(300, 40), fullBands,
            SpectrumDisplayMode.BottomUp, bottomPadding);
        var centeredRects = SpectrumBarLayout.Calculate(new Size(300, 40), fullBands,
            SpectrumDisplayMode.Centered, centeredPadding);
        Assert.All(bottomRects, rectangle => Assert.True(rectangle.Bottom <= 37));
        Assert.All(centeredRects, rectangle => Assert.Equal(40, rectangle.Bottom, 6));
    }

    [AvaloniaFact]
    public void RepeatedNinetySixDetailGlowFramesReuseBuffers()
    {
        var firstProvider = new SpectrumFrameProvider();
        var secondProvider = new SpectrumFrameProvider();
        var firstControl = CreateControl(firstProvider, SpectrumVisualizationStyle.Bars,
            SpectrumDisplayMode.BottomUp, 96, new Size(300, 64));
        var secondControl = CreateControl(secondProvider, SpectrumVisualizationStyle.FilledCurve,
            SpectrumDisplayMode.Centered, 96, new Size(300, 64));
        using var firstBitmap = new RenderTargetBitmap(new PixelSize(300, 64), new Vector(96, 96));
        using var secondBitmap = new RenderTargetBitmap(new PixelSize(300, 64), new Vector(96, 96));
        Geometry? upperGeometry = null;
        Geometry? lowerGeometry = null;
        Geometry? centeredFillGeometry = null;
        var geometryGeneration = 0;

        for (var frameIndex = 0; frameIndex < 600; frameIndex++)
        {
            var frame = CreateFrame(frameIndex);
            firstProvider.Publish(frame);
            secondProvider.Publish(frame);
            firstBitmap.Render(firstControl);
            secondBitmap.Render(secondControl);
            if (frameIndex == 0)
            {
                upperGeometry = secondControl.CurveUpperGeometry;
                lowerGeometry = secondControl.CurveLowerGeometry;
                centeredFillGeometry = secondControl.CurveCenteredFillGeometry;
                geometryGeneration = secondControl.CurveGeometryGeneration;
            }
        }

        Assert.Equal(96, firstControl.ResampleBufferCapacity);
        Assert.Equal(96, firstControl.BarRectangleBufferCapacity);
        Assert.Equal(96, secondControl.ResampleBufferCapacity);
        Assert.Equal(96, secondControl.CurvePointBufferCapacity);
        Assert.Equal(geometryGeneration, secondControl.CurveGeometryGeneration);
        Assert.Same(upperGeometry!, secondControl.CurveUpperGeometry);
        Assert.Same(lowerGeometry!, secondControl.CurveLowerGeometry);
        Assert.Same(centeredFillGeometry!, secondControl.CurveCenteredFillGeometry);
    }

    [AvaloniaTheory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void DynamicBarGradientUpdatesEveryRenderedFrameAndReusesPaletteObjects(int frameRate)
    {
        var provider = new SpectrumFrameProvider();
        provider.Publish(CreateFrame(0));
        var control = new SpectrumBarsControl
        {
            Width = 300,
            Height = 64,
            BarBrush = Brushes.Magenta
        };
        control.Initialize(provider);
        control.SetEffectiveFrameRate(frameRate);
        control.SetComponentSettings(new SpectrumComponentSettings
        {
            VisualizationStyle = SpectrumVisualizationStyle.Bars,
            GradientMode = SpectrumGradientMode.Dynamic,
            GradientSpeed = SpectrumGradientSpeed.Slow,
            GlowEnabled = true,
            GlowIntensity = 0.5,
            BarCount = 48
        });
        control.Measure(new Size(300, 64));
        control.Arrange(new Rect(0, 0, 300, 64));
        using var bitmap = new RenderTargetBitmap(new PixelSize(300, 64), new Vector(96, 96));

        bitmap.Render(control);
        var barPalette = control.CachedBarPalette;
        var laserPalette = control.CachedLaserHighlightPalette;
        var barUpdates = control.BarPaletteUpdateCount;
        var laserUpdates = control.LaserPaletteUpdateCount;
        bitmap.Render(control);

        Assert.Same(barPalette, control.CachedBarPalette);
        Assert.Same(laserPalette, control.CachedLaserHighlightPalette);
        Assert.Equal(barUpdates + 1, control.BarPaletteUpdateCount);
        Assert.Equal(laserUpdates + 1, control.LaserPaletteUpdateCount);
    }

    [AvaloniaFact]
    public void StaticColorTransitionMutatesCachedBrushesAndPalettesInPlace()
    {
        var start = new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);
        var time = new ManualTimeProvider(start);
        var provider = new SpectrumFrameProvider();
        provider.Publish(CreateFrame(0));
        var settings = new SpectrumComponentSettings
        {
            VisualizationStyle = SpectrumVisualizationStyle.Bars,
            ColorSource = SpectrumColorSource.Custom,
            CustomColor = Colors.Red,
            UseCustomGradientEndColor = true,
            GradientEndColor = Colors.Blue,
            GradientMode = SpectrumGradientMode.Static,
            GlowEnabled = true,
            GlowIntensity = 0.5,
            BarCount = 48
        };
        var control = new SpectrumBarsControl { Width = 300, Height = 64 };
        control.SetTimeProvider(time);
        control.Initialize(provider);
        control.SetComponentSettings(settings);
        control.Measure(new Size(300, 64));
        control.Arrange(new Rect(0, 0, 300, 64));
        using var bitmap = new RenderTargetBitmap(new PixelSize(300, 64), new Vector(96, 96));

        bitmap.Render(control);
        var brush = control.CachedBrush;
        var laserBrush = control.CachedLaserHighlightBrush;
        var palette = control.CachedBarPalette;
        var laserPalette = control.CachedLaserHighlightPalette;
        var firstPaletteBrush = palette[0];
        settings.CustomColor = Colors.Lime;
        settings.GradientEndColor = Colors.Yellow;
        bitmap.Render(control);
        time.Advance(TimeSpan.FromMilliseconds(400));
        bitmap.Render(control);
        var midpoint = control.LastResolvedPalette!;
        time.Advance(TimeSpan.FromMilliseconds(400));
        bitmap.Render(control);

        Assert.Same(brush, control.CachedBrush);
        Assert.Same(laserBrush, control.CachedLaserHighlightBrush);
        Assert.Same(palette, control.CachedBarPalette);
        Assert.Same(laserPalette, control.CachedLaserHighlightPalette);
        Assert.Same(firstPaletteBrush, control.CachedBarPalette[0]);
        Assert.NotEqual(Colors.Red, midpoint.Primary);
        Assert.NotEqual(Colors.Lime, midpoint.Primary);
        Assert.Equal(Colors.Lime, control.LastResolvedPalette!.Primary);
        Assert.Equal(Colors.Yellow, control.LastResolvedPalette.Secondary);
    }

    [AvaloniaFact]
    public void CurveColorTransitionReusesBrushesAndPens()
    {
        var start = new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);
        var time = new ManualTimeProvider(start);
        var provider = new SpectrumFrameProvider();
        provider.Publish(CreateFrame(0));
        var settings = new SpectrumComponentSettings
        {
            VisualizationStyle = SpectrumVisualizationStyle.SmoothLine,
            ColorSource = SpectrumColorSource.Custom,
            CustomColor = Colors.Red,
            GradientMode = SpectrumGradientMode.Off,
            GlowEnabled = true,
            GlowIntensity = 0.5,
            BarCount = 48
        };
        var control = new SpectrumBarsControl { Width = 300, Height = 64 };
        control.SetTimeProvider(time);
        control.Initialize(provider);
        control.SetComponentSettings(settings);
        control.Measure(new Size(300, 64));
        control.Arrange(new Rect(0, 0, 300, 64));
        using var bitmap = new RenderTargetBitmap(new PixelSize(300, 64), new Vector(96, 96));

        bitmap.Render(control);
        var brush = control.CachedBrush;
        var highlightBrush = control.CachedLaserHighlightBrush;
        var pen = control.CachedCurvePen;
        settings.CustomColor = Colors.Lime;
        bitmap.Render(control);
        time.Advance(TimeSpan.FromMilliseconds(400));
        bitmap.Render(control);
        time.Advance(TimeSpan.FromMilliseconds(400));
        bitmap.Render(control);

        Assert.Same(brush, control.CachedBrush);
        Assert.Same(highlightBrush, control.CachedLaserHighlightBrush);
        Assert.Same(pen, control.CachedCurvePen);
        Assert.Equal(Colors.Lime, control.LastResolvedPalette!.Primary);
    }

    [AvaloniaFact]
    public void ReusableCurveCacheCutsManagedAllocationsByAtLeastEightyPercent()
    {
        var bands = Enumerable.Range(0, 96)
            .Select(index => (float)(0.05 + 0.90 * Math.Abs(Math.Sin(index * 0.17))))
            .ToArray();
        var cache = new SpectrumCurveGeometryCache();
        Assert.True(cache.Update(
            new Size(300, 64), bands, SpectrumDisplayMode.Centered, default, includeFill: true));

        var cachedAllocations = MeasureAllocatedBytes(() =>
        {
            for (var frame = 0; frame < 600; frame++)
                cache.Update(new Size(300, 64), bands, SpectrumDisplayMode.Centered, default, includeFill: true);
        });
        var allocatingBaseline = MeasureAllocatedBytes(() =>
        {
            for (var frame = 0; frame < 600; frame++)
            {
                var points = SpectrumCurveLayout.Calculate(
                    new Size(300, 64), bands, SpectrumDisplayMode.Centered);
                var fill = SpectrumCurveGeometryBuilder.CreateCenteredFill(points.Upper, points.Lower);
                var upper = SpectrumCurveGeometryBuilder.CreateOpenCurve(points.Upper);
                var lower = SpectrumCurveGeometryBuilder.CreateOpenCurve(points.Lower);
                GC.KeepAlive(fill);
                GC.KeepAlive(upper);
                GC.KeepAlive(lower);
            }
        });

        _output.WriteLine(
            "600 帧曲线更新：缓存路径 {0:N0} 字节；旧路径 {1:N0} 字节；下降 {2:P1}。",
            cachedAllocations,
            allocatingBaseline,
            1 - cachedAllocations / (double)allocatingBaseline);

        Assert.True(
            cachedAllocations <= allocatingBaseline * 0.20,
            $"缓存路径分配 {cachedAllocations:N0} 字节，旧路径分配 {allocatingBaseline:N0} 字节。");
    }

    private static RenderTargetBitmap Render(
        SpectrumVisualizationStyle style,
        SpectrumDisplayMode mode,
        int detailCount,
        Size size,
        double amplitude)
    {
        var provider = new SpectrumFrameProvider();
        provider.Publish(CreateFrame(0));
        var control = CreateControl(provider, style, mode, detailCount, size, amplitude);
        var bitmap = new RenderTargetBitmap(
            new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width)), Math.Max(1, (int)Math.Ceiling(size.Height))),
            new Vector(96, 96));
        bitmap.Render(control);
        return bitmap;
    }

    private static SpectrumBarsControl CreateControl(
        SpectrumFrameProvider provider,
        SpectrumVisualizationStyle style,
        SpectrumDisplayMode mode,
        int detailCount,
        Size size,
        double amplitude = 1)
    {
        var control = new SpectrumBarsControl
        {
            Width = size.Width,
            Height = size.Height,
            BarBrush = Brushes.Magenta
        };
        control.Initialize(provider);
        control.SetComponentSettings(new SpectrumComponentSettings
        {
            VisualizationStyle = style,
            DisplayMode = mode,
            BarCount = detailCount,
            Amplitude = amplitude,
            Opacity = 0.65,
            GradientEnabled = true,
            GlowEnabled = true,
            GlowIntensity = 0.75
        });
        control.Measure(size);
        control.Arrange(new Rect(size));
        return control;
    }

    private static SpectrumFrame CreateFrame(int offset) => new(
        Enumerable.Range(0, 96)
            .Select(index => (float)(0.05 + 0.90 * Math.Abs(Math.Sin((index + offset) * 0.17))))
            .ToArray(),
        DateTimeOffset.UtcNow,
        false);

    private static long MeasureAllocatedBytes(Action action)
    {
        action();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        internal void Advance(TimeSpan duration) => _utcNow += duration;
    }

}
