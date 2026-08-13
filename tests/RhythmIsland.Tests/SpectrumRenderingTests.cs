using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using RhythmIsland.Controls.Components;
using RhythmIsland.Models;
using RhythmIsland.Services;

namespace RhythmIsland.Tests;

public sealed class SpectrumRenderingTests
{
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

        for (var frameIndex = 0; frameIndex < 600; frameIndex++)
        {
            var frame = CreateFrame(frameIndex);
            firstProvider.Publish(frame);
            secondProvider.Publish(frame);
            firstBitmap.Render(firstControl);
            secondBitmap.Render(secondControl);
        }

        Assert.Equal(96, firstControl.ResampleBufferCapacity);
        Assert.Equal(96, firstControl.BarRectangleBufferCapacity);
        Assert.Equal(96, secondControl.ResampleBufferCapacity);
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

}
