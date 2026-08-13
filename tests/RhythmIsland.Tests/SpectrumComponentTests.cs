using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using ClassIsland.Core.Attributes;
using RhythmIsland.Abstractions;
using RhythmIsland.Controls.Components;
using RhythmIsland.Models;
using RhythmIsland.Services;

namespace RhythmIsland.Tests;

public sealed class SpectrumComponentTests
{
    [Theory]
    [InlineData(SpectrumDisplayMode.BottomUp)]
    [InlineData(SpectrumDisplayMode.Centered)]
    public void LayoutStaysInsideBounds(SpectrumDisplayMode mode)
    {
        var rectangles = SpectrumBarLayout.Calculate(new Size(300, 64), Enumerable.Repeat(1f, 48).ToArray(), mode);
        Assert.Equal(48, rectangles.Count);
        Assert.All(rectangles, rectangle =>
        {
            Assert.True(double.IsFinite(rectangle.X) && double.IsFinite(rectangle.Y));
            Assert.InRange(rectangle.Left, 0, 300);
            Assert.InRange(rectangle.Right, 0, 300);
            Assert.InRange(rectangle.Top, 0, 64);
            Assert.InRange(rectangle.Bottom, 0, 64);
        });
    }

    [Fact]
    public void ZeroAndInvalidSizesProduceNoBars()
    {
        Assert.Empty(SpectrumBarLayout.Calculate(new Size(0, 20), [1f], SpectrumDisplayMode.BottomUp));
        Assert.Empty(SpectrumBarLayout.Calculate(new Size(20, 0), [1f], SpectrumDisplayMode.BottomUp));
        Assert.Empty(SpectrumBarLayout.Calculate(new Size(double.NaN, 20), [1f], SpectrumDisplayMode.BottomUp));
    }

    [Fact]
    public void ExtremelyNarrowLayoutRemainsFiniteAndNonNegative()
    {
        var rectangles = SpectrumBarLayout.Calculate(new Size(1, 20), Enumerable.Repeat(0.5f, 96).ToArray(), SpectrumDisplayMode.Centered);
        Assert.Equal(96, rectangles.Count);
        Assert.All(rectangles, rectangle =>
        {
            Assert.True(double.IsFinite(rectangle.X) && double.IsFinite(rectangle.Width));
            Assert.True(rectangle.Width >= 0 && rectangle.Right <= 1.000001);
        });
    }

    [Fact]
    public void SilenceFloorRemovesDegenerateBottomBar()
    {
        var rectangles = SpectrumBarLayout.Calculate(
            new Size(100, 40),
            [SpectrumAnalyzer.SilenceFloor / 2, 1f],
            SpectrumDisplayMode.BottomUp);

        Assert.Equal(2, rectangles.Count);
        Assert.Equal(0, rectangles[0].Height);
        Assert.Equal(40, rectangles[1].Bottom);
    }

    [Fact]
    public void BottomUpPaddingKeepsBarsInsideRoundedHost()
    {
        var rectangles = SpectrumBarLayout.Calculate(
            new Size(100, 40),
            [1f, 0.5f],
            SpectrumDisplayMode.BottomUp,
            new Thickness(1, 1, 1, 3));

        Assert.All(rectangles, rectangle =>
        {
            Assert.True(rectangle.Left >= 1);
            Assert.True(rectangle.Right <= 99);
            Assert.True(rectangle.Top >= 1);
            Assert.True(rectangle.Bottom <= 37);
        });
    }

    [Fact]
    public void CenteredLayoutKeepsOriginalDrawingArea()
    {
        var rectangle = Assert.Single(SpectrumBarLayout.Calculate(
            new Size(100, 40),
            [1f],
            SpectrumDisplayMode.Centered));

        Assert.Equal(0, rectangle.Top);
        Assert.Equal(40, rectangle.Bottom);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(64)]
    [InlineData(96)]
    public void EveryAllowedBarCountProducesMatchingGeometry(int barCount)
    {
        var rectangles = SpectrumBarLayout.Calculate(new Size(240, 50), new float[barCount], SpectrumDisplayMode.BottomUp);
        Assert.Equal(barCount, rectangles.Count);
    }

    [Fact]
    public void WidthChangeRecalculatesGeometry()
    {
        var bands = Enumerable.Repeat(1f, 24).ToArray();
        var narrow = SpectrumBarLayout.Calculate(new Size(120, 40), bands, SpectrumDisplayMode.BottomUp);
        var wide = SpectrumBarLayout.Calculate(new Size(300, 40), bands, SpectrumDisplayMode.BottomUp);
        Assert.True(wide[0].Width > narrow[0].Width);
        Assert.Equal(2.5, wide[0].Width / narrow[0].Width, 6);
        Assert.Equal(2.5, wide[^1].Right / narrow[^1].Right, 6);
        Assert.True(narrow[^1].Right <= 120 && wide[^1].Right <= 300);
    }

    [Fact]
    public void InvalidDisplayModeFallsBackAndColorIsStored()
    {
        var settings = new SpectrumComponentSettings
        {
            VisualizationStyle = (SpectrumVisualizationStyle)999,
            DisplayMode = (SpectrumDisplayMode)999,
            UseCustomColor = true,
            CustomColor = Colors.Orange
        };
        Assert.Equal(SpectrumVisualizationStyle.Bars, settings.VisualizationStyle);
        Assert.Equal(SpectrumDisplayMode.BottomUp, settings.DisplayMode);
        Assert.True(settings.UseCustomColor);
        Assert.Equal(Colors.Orange, settings.CustomColor);
    }

    [Fact]
    public void ComponentColorSurvivesJsonRoundTrip()
    {
        var source = new SpectrumComponentSettings
        {
            VisualizationStyle = SpectrumVisualizationStyle.FilledCurve,
            UseCustomColor = true,
            CustomColor = Color.Parse("#FF12ABEF"),
            GradientEnabled = true,
            UseCustomGradientEndColor = true,
            GradientEndColor = Color.Parse("#FF6543EF"),
            GlowEnabled = true,
            GlowIntensity = 0.75,
            FrameRate = 120,
            BarCount = 64,
            Amplitude = 1.75,
            Opacity = 0.4,
            Width = 360,
            AutoCollapseEnabled = false,
            SilenceCollapseDelaySeconds = 22
        };
        var json = JsonSerializer.Serialize(source);
        var restored = JsonSerializer.Deserialize<SpectrumComponentSettings>(json);
        Assert.NotNull(restored);
        Assert.Equal(SpectrumVisualizationStyle.FilledCurve, restored!.VisualizationStyle);
        Assert.True(restored.UseCustomColor);
        Assert.Equal(source.CustomColor, restored.CustomColor);
        Assert.True(restored.GradientEnabled);
        Assert.True(restored.UseCustomGradientEndColor);
        Assert.Equal(source.GradientEndColor, restored.GradientEndColor);
        Assert.True(restored.GlowEnabled);
        Assert.Equal(0.75, restored.GlowIntensity);
        Assert.Equal(120, restored.FrameRate);
        Assert.Equal(64, restored.BarCount);
        Assert.Equal(1.75, restored.Amplitude);
        Assert.Equal(0.4, restored.Opacity);
        Assert.Equal(360, restored.Width);
        Assert.False(restored.AutoCollapseEnabled);
        Assert.Equal(22, restored.SilenceCollapseDelaySeconds);
    }

    [Fact]
    public void InvalidInstanceLayoutValuesFallBackOrClamp()
    {
        var settings = new SpectrumComponentSettings
        {
            BarCount = 17,
            Amplitude = double.NaN,
            Opacity = double.NaN,
            GlowIntensity = double.NaN,
            Width = double.PositiveInfinity,
            SilenceCollapseDelaySeconds = double.NaN
        };
        Assert.Equal(48, settings.BarCount);
        Assert.Equal(1, settings.Amplitude);
        Assert.Equal(1, settings.Opacity);
        Assert.Equal(0.50, settings.GlowIntensity);
        Assert.Equal(240, settings.Width);
        Assert.True(settings.AutoCollapseEnabled);
        Assert.Equal(5, settings.SilenceCollapseDelaySeconds);
        settings.Width = 10;
        settings.Amplitude = 8;
        settings.Opacity = 5;
        settings.GlowIntensity = 5;
        Assert.Equal(60, settings.Width);
        Assert.Equal(3, settings.Amplitude);
        Assert.Equal(1, settings.Opacity);
        Assert.Equal(1, settings.GlowIntensity);
        settings.SilenceCollapseDelaySeconds = 200;
        Assert.Equal(120, settings.SilenceCollapseDelaySeconds);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(64)]
    [InlineData(96)]
    public void NinetySixAnalysisBandsCanFeedEveryComponentBarCount(int targetCount)
    {
        var source = Enumerable.Range(0, 96).Select(index => index / 95f).ToArray();
        var result = SpectrumBandResampler.Resample(source, targetCount);
        Assert.Equal(targetCount, result.Count);
        Assert.All(result, value => Assert.True(float.IsFinite(value) && value is >= 0 and <= 1));
        Assert.True(result[^1] >= result[0]);
    }

    [Fact]
    public void ComponentAmplitudeScalesAndClampsBands()
    {
        var source = new float[] { 0.2f, 0.6f };
        var quiet = SpectrumBandResampler.Resample(source, 2, 0.5);
        var loud = SpectrumBandResampler.Resample(source, 2, 2.0);

        Assert.Equal(0.1f, quiet[0], 5);
        Assert.Equal(0.3f, quiet[1], 5);
        Assert.Equal(0.4f, loud[0], 5);
        Assert.Equal(1f, loud[1]);
    }

    [Fact]
    public void BarCountSelectionUsesOnlySupportedCounts()
    {
        var settings = new SpectrumComponentSettings();
        foreach (var barCount in RhythmIslandSettings.AllowedBarCounts)
        {
            settings.BarCount = barCount;
            Assert.Equal(barCount, settings.BarCount);
        }
    }

    [Fact]
    public void InvalidPersistedComponentColorFallsBackToDefault()
    {
        var settings = new SpectrumComponentSettings
        {
            CustomColorText = "invalid",
            GradientEndColorText = "also-invalid"
        };
        Assert.Equal(Color.Parse("#FF4DA3FF"), settings.CustomColor);
        Assert.Equal("#FF4DA3FF", settings.CustomColorText);
        Assert.Equal(Color.Parse("#FF9B5DE5"), settings.GradientEndColor);
        Assert.Equal("#FF9B5DE5", settings.GradientEndColorText);
    }

    [Fact]
    public void OldComponentJsonKeepsBarsAndEffectsDisabled()
    {
        const string oldJson = """
            {"DisplayMode":1,"BarCount":32,"Opacity":0.5,"Width":300}
            """;

        var restored = JsonSerializer.Deserialize<SpectrumComponentSettings>(oldJson);

        Assert.NotNull(restored);
        Assert.Equal(SpectrumVisualizationStyle.Bars, restored!.VisualizationStyle);
        Assert.Equal(SpectrumDisplayMode.Centered, restored.DisplayMode);
        Assert.False(restored.GradientEnabled);
        Assert.False(restored.UseCustomGradientEndColor);
        Assert.False(restored.GlowEnabled);
        Assert.Equal(0.50, restored.GlowIntensity);
        Assert.Equal(30, restored.FrameRate);
    }

    [Theory]
    [InlineData(true, true, SpectrumColorSource.Custom, SpectrumGradientMode.Static)]
    [InlineData(false, true, SpectrumColorSource.ThemeAccent, SpectrumGradientMode.Static)]
    [InlineData(false, false, SpectrumColorSource.ThemeAccent, SpectrumGradientMode.Off)]
    public void LegacyColorFlagsMigrateToExplicitModes(
        bool useCustomColor,
        bool gradientEnabled,
        SpectrumColorSource expectedColorSource,
        SpectrumGradientMode expectedGradientMode)
    {
        var json = $$"""
            {"UseCustomColor":{{useCustomColor.ToString().ToLowerInvariant()}},"GradientEnabled":{{gradientEnabled.ToString().ToLowerInvariant()}},"Opacity":0.65}
            """;

        var restored = JsonSerializer.Deserialize<SpectrumComponentSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal(expectedColorSource, restored!.ColorSource);
        Assert.Equal(expectedGradientMode, restored.GradientMode);
        Assert.Equal(0.65, restored.Opacity);
    }

    [Fact]
    public void NewColorModesAndGradientSpeedSurviveRoundTrip()
    {
        var settings = new SpectrumComponentSettings
        {
            ColorSource = SpectrumColorSource.MediaCover,
            MediaCoverColorMode = SpectrumMediaColorMode.Tinted,
            GradientMode = SpectrumGradientMode.Dynamic,
            GradientSpeed = SpectrumGradientSpeed.Fast
        };

        var json = JsonSerializer.Serialize(settings);
        var restored = JsonSerializer.Deserialize<SpectrumComponentSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal(SpectrumColorSource.MediaCover, restored!.ColorSource);
        Assert.Equal(SpectrumMediaColorMode.Tinted, restored.MediaCoverColorMode);
        Assert.Equal(SpectrumGradientMode.Dynamic, restored.GradientMode);
        Assert.Equal(SpectrumGradientSpeed.Fast, restored.GradientSpeed);
        Assert.DoesNotContain("UseCustomColor", json);
        Assert.DoesNotContain("GradientEnabled", json);
    }

    [Fact]
    public void InvalidColorModesFallBackAndNewComponentsAreOpaque()
    {
        var settings = new SpectrumComponentSettings
        {
            ColorSource = (SpectrumColorSource)999,
            MediaCoverColorMode = (SpectrumMediaColorMode)999,
            GradientMode = (SpectrumGradientMode)999,
            GradientSpeed = (SpectrumGradientSpeed)999
        };

        Assert.Equal(SpectrumColorSource.ThemeAccent, settings.ColorSource);
        Assert.Equal(SpectrumMediaColorMode.Vivid, settings.MediaCoverColorMode);
        Assert.Equal(SpectrumGradientMode.Off, settings.GradientMode);
        Assert.Equal(SpectrumGradientSpeed.Medium, settings.GradientSpeed);
        Assert.Equal(1, settings.Opacity);
    }

    [Fact]
    public void OldComponentJsonDefaultsToVividMediaColorMode()
    {
        const string json = """
            {"ColorSource":1,"GradientMode":2,"Opacity":0.65}
            """;

        var restored = JsonSerializer.Deserialize<SpectrumComponentSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal(SpectrumMediaColorMode.Vivid, restored!.MediaCoverColorMode);
        Assert.Equal(0.65, restored.Opacity);
    }

    [Theory]
    [InlineData(SpectrumDisplayMode.BottomUp)]
    [InlineData(SpectrumDisplayMode.Centered)]
    public void CurvePointsStayFiniteAndInsideBounds(SpectrumDisplayMode mode)
    {
        var bands = Enumerable.Range(0, 96)
            .Select(index => index % 11 == 0 ? float.NaN : index / 95f)
            .ToArray();
        var points = SpectrumCurveLayout.Calculate(
            new Size(300, 64),
            bands,
            mode,
            mode == SpectrumDisplayMode.BottomUp ? new Thickness(1, 1, 1, 3) : default);

        Assert.Equal(96, points.Upper.Count);
        Assert.Equal(mode == SpectrumDisplayMode.Centered ? 96 : 0, points.Lower.Count);
        Assert.All(points.Upper.Concat(points.Lower), point =>
        {
            Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y));
            Assert.InRange(point.X, points.DrawingBounds.Left, points.DrawingBounds.Right);
            Assert.InRange(point.Y, points.DrawingBounds.Top, points.DrawingBounds.Bottom);
        });
    }

    [Fact]
    public void CurveLayoutHandlesZeroAndExtremelyNarrowSizes()
    {
        Assert.True(SpectrumCurveLayout.Calculate(
            new Size(0, 40), [1f], SpectrumDisplayMode.BottomUp).IsEmpty);

        var points = SpectrumCurveLayout.Calculate(
            new Size(1, 40),
            Enumerable.Repeat(0.5f, 96).ToArray(),
            SpectrumDisplayMode.Centered);
        Assert.Equal(96, points.Upper.Count);
        Assert.All(points.Upper.Concat(points.Lower), point => Assert.InRange(point.X, 0, 1));
    }

    [Theory]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(64)]
    [InlineData(96)]
    public void EveryCurveDetailCountBuildsBothDirections(int detailCount)
    {
        var bands = Enumerable.Repeat(0.75f, detailCount).ToArray();
        foreach (var mode in Enum.GetValues<SpectrumDisplayMode>())
        {
            var points = SpectrumCurveLayout.Calculate(new Size(240, 50), bands, mode);
            Assert.Equal(detailCount, points.Upper.Count);
            Assert.Equal(mode == SpectrumDisplayMode.Centered ? detailCount : 0, points.Lower.Count);
            var smoothSegments = SpectrumCurveGeometryBuilder.CalculateSmoothSegments(points.Upper);
            Assert.Equal(detailCount - 1, smoothSegments.Count);
            Assert.All(smoothSegments, segment =>
            {
                Assert.InRange(segment.Control.X, points.DrawingBounds.Left, points.DrawingBounds.Right);
                Assert.InRange(segment.Control.Y, points.DrawingBounds.Top, points.DrawingBounds.Bottom);
                Assert.InRange(segment.End.X, points.DrawingBounds.Left, points.DrawingBounds.Right);
                Assert.InRange(segment.End.Y, points.DrawingBounds.Top, points.DrawingBounds.Bottom);
            });
            if (mode == SpectrumDisplayMode.Centered)
            {
                var boundary = SpectrumCurveGeometryBuilder.CalculateCenteredFillBoundary(points.Upper, points.Lower);
                Assert.Equal(detailCount * 2, boundary.Count);
            }
            else
            {
                var boundary = SpectrumCurveGeometryBuilder.CalculateBottomFillBoundary(points.Upper, points.DrawingBounds);
                Assert.Equal(detailCount + 2, boundary.Count);
            }
        }
    }

    [Fact]
    public void CenteredCurveIsMirroredAroundOriginalCenter()
    {
        var points = SpectrumCurveLayout.Calculate(
            new Size(120, 40),
            [0f, 0.25f, 1f],
            SpectrumDisplayMode.Centered);

        for (var index = 0; index < points.Upper.Count; index++)
            Assert.Equal(40, points.Upper[index].Y + points.Lower[index].Y, 6);
        Assert.Equal(0, points.Upper[^1].Y);
        Assert.Equal(40, points.Lower[^1].Y);
    }

    [Fact]
    public void BottomCurveUsesRoundedHostSafetyPadding()
    {
        var points = SpectrumCurveLayout.Calculate(
            new Size(100, 40),
            [0f, 1f],
            SpectrumDisplayMode.BottomUp,
            new Thickness(1, 1, 1, 3));

        Assert.Equal(new Rect(1, 1, 98, 36), points.DrawingBounds);
        Assert.All(points.Upper, point =>
        {
            Assert.InRange(point.X, 1, 99);
            Assert.InRange(point.Y, 1, 37);
        });
    }

    [Fact]
    public void CurveFillBoundariesCloseToExpectedEdges()
    {
        var bottom = SpectrumCurveLayout.Calculate(
            new Size(100, 40), [1f, 0.5f, 1f], SpectrumDisplayMode.BottomUp);
        var bottomBoundary = SpectrumCurveGeometryBuilder.CalculateBottomFillBoundary(bottom.Upper, bottom.DrawingBounds);
        Assert.Equal(new Point(100, 40), bottomBoundary[^2]);
        Assert.Equal(new Point(0, 40), bottomBoundary[^1]);

        var centered = SpectrumCurveLayout.Calculate(
            new Size(100, 40), [1f, 0.5f, 1f], SpectrumDisplayMode.Centered);
        var centeredBoundary = SpectrumCurveGeometryBuilder.CalculateCenteredFillBoundary(centered.Upper, centered.Lower);
        Assert.Contains(centeredBoundary, point => point.Y == 0);
        Assert.Contains(centeredBoundary, point => point.Y == 40);
        Assert.Equal(centered.Upper[0], centeredBoundary[0]);
        Assert.Equal(centered.Lower[^1], centeredBoundary[centered.Upper.Count]);
    }

    [Fact]
    public void AutomaticGradientUsesNeighborHueAndGrayscaleFallback()
    {
        var red = Color.Parse("#FFEF3340");
        var shifted = SpectrumColorHelper.CreateAutomaticGradientEnd(red);
        Assert.Equal(red.A, shifted.A);
        Assert.NotEqual(red, shifted);

        var gray = Color.Parse("#80FFFFFF");
        Assert.Equal(Color.Parse("#804DA3FF"), SpectrumColorHelper.CreateAutomaticGradientEnd(gray));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void GradientIsHorizontalAndLaserGlowLayersAreOrdered()
    {
        var brush = SpectrumVisualEffectHelper.CreateHorizontalGradient(Colors.Blue, Colors.Purple);
        Assert.Equal(new RelativePoint(0, 0, RelativeUnit.Relative), brush.StartPoint);
        Assert.Equal(new RelativePoint(1, 0, RelativeUnit.Relative), brush.EndPoint);
        Assert.Equal(2, brush.GradientStops.Count);

        var minimum = SpectrumVisualEffectHelper.CalculateLaserGlow(-5);
        var maximum = SpectrumVisualEffectHelper.CalculateLaserGlow(5);
        var fallback = SpectrumVisualEffectHelper.CalculateLaserGlow(double.NaN);

        Assert.Equal(3.7, minimum.OuterSpread, 6);
        Assert.Equal(10, maximum.OuterSpread, 6);
        Assert.Equal(6.5, fallback.OuterSpread, 6);
        foreach (var glow in new[] { minimum, maximum, fallback })
        {
            Assert.True(glow.OuterSpread > glow.MiddleSpread);
            Assert.True(glow.MiddleSpread > glow.InnerSpread);
            Assert.True(glow.OuterOpacity < glow.MiddleOpacity);
            Assert.True(glow.MiddleOpacity < glow.InnerOpacity);
            Assert.InRange(glow.HighlightWidthRatio, 0.70, 0.80);
            Assert.InRange(glow.HighlightLightenAmount, 0.20 - 0.01, 0.35);
            Assert.InRange(glow.HighlightOpacity, 0, 1);
        }

        Assert.True(maximum.OuterSpread > minimum.OuterSpread);
        Assert.True(maximum.OuterOpacity > minimum.OuterOpacity);
        Assert.True(maximum.HighlightLightenAmount > minimum.HighlightLightenAmount);
    }

    [Fact]
    public void LaserHighlightIsWideCenteredAndPreservesBarHeight()
    {
        var rectangle = new Rect(10, 5, 8, 20);
        foreach (var intensity in new[] { 0.10, 0.50, 1.00 })
        {
            var glow = SpectrumVisualEffectHelper.CalculateLaserGlow(intensity);
            var highlight = SpectrumVisualEffectHelper.CalculateBarHighlight(rectangle, glow);

            Assert.InRange(highlight.Width / rectangle.Width, 0.70, 0.80);
            Assert.Equal(rectangle.Center.X, highlight.Center.X, 6);
            Assert.Equal(rectangle.Top, highlight.Top, 6);
            Assert.Equal(rectangle.Bottom, highlight.Bottom, 6);
            Assert.True(highlight.Left >= rectangle.Left && highlight.Right <= rectangle.Right);
        }

        Assert.Equal(default, SpectrumVisualEffectHelper.CalculateBarHighlight(
            new Rect(0, 0, 0, 20),
            SpectrumVisualEffectHelper.CalculateLaserGlow(0.5)));
    }

    [Fact]
    public void LaserHighlightColorIsModeratelyBrighterAndPreservesAlpha()
    {
        var source = Color.Parse("#80402080");
        var highlight = SpectrumColorHelper.Lighten(source, 0.35);

        Assert.Equal(source.A, highlight.A);
        Assert.True(highlight.R > source.R && highlight.R < 255);
        Assert.True(highlight.G > source.G && highlight.G < 255);
        Assert.True(highlight.B > source.B && highlight.B < 255);
        Assert.Equal(source, SpectrumColorHelper.Lighten(source, double.NaN));
    }

    [Fact]
    public void VisibleSignalCheckRejectsSilentAndInvalidBands()
    {
        Assert.False(SpectrumBandResampler.HasVisibleSignal([]));
        Assert.False(SpectrumBandResampler.HasVisibleSignal([0f, 0.004f, float.NaN]));
        Assert.True(SpectrumBandResampler.HasVisibleSignal([0f, 0.005f]));
        Assert.True(SpectrumBandResampler.HasVisibleSignal([0f, 0.5f]));
    }

    [Fact]
    public void BarGradientInterpolationReachesBothEndpointColors()
    {
        var start = Color.Parse("#FF000000");
        var end = Color.Parse("#FFFFFFFF");

        Assert.Equal(start, SpectrumColorHelper.Interpolate(start, end, 0));
        Assert.Equal(Color.Parse("#FF808080"), SpectrumColorHelper.Interpolate(start, end, 0.5));
        Assert.Equal(end, SpectrumColorHelper.Interpolate(start, end, 1));
        Assert.Equal(start, SpectrumColorHelper.Interpolate(start, end, double.NaN));
    }

    [Theory]
    [InlineData(SpectrumGradientSpeed.Slow, 10)]
    [InlineData(SpectrumGradientSpeed.Medium, 6)]
    [InlineData(SpectrumGradientSpeed.Fast, 3)]
    public void DynamicGradientSpeedHasExpectedSeamlessPeriod(SpectrumGradientSpeed speed, double seconds)
    {
        Assert.Equal(seconds, SpectrumColorHelper.DynamicGradientPeriodSeconds(speed));
        Assert.Equal(0, SpectrumColorHelper.DynamicGradientPhase(speed, TimeSpan.Zero));
        Assert.Equal(0, SpectrumColorHelper.DynamicGradientPhase(speed, TimeSpan.FromSeconds(seconds)), 6);

        var primary = Colors.Red;
        var secondary = Colors.Blue;
        Assert.Equal(
            SpectrumColorHelper.SampleDynamicGradient(primary, secondary, 0, 0),
            SpectrumColorHelper.SampleDynamicGradient(primary, secondary, 1, 0));
    }

    [Fact]
    public void PaletteTransitionBlendsForEightHundredMilliseconds()
    {
        var transition = new SpectrumPaletteTransition();
        var start = DateTimeOffset.UtcNow;
        var first = new SpectrumPalette(Colors.Red, Colors.Blue);
        var second = new SpectrumPalette(Colors.Green, Colors.Yellow);

        Assert.Equal(first, transition.Resolve(first, start));
        Assert.Equal(first, transition.Resolve(second, start));
        var middle = transition.Resolve(second, start.AddMilliseconds(400));
        Assert.Equal(SpectrumColorHelper.Interpolate(Colors.Red, Colors.Green, 0.5), middle.Primary);
        Assert.Equal(second, transition.Resolve(second, start.AddMilliseconds(800)));
    }

    [Fact]
    public void AutoCollapseWaitsForSilenceAndRecoversOnAudio()
    {
        var settings = new SpectrumComponentSettings { SilenceCollapseDelaySeconds = 2 };
        var state = new SpectrumAutoCollapseState();
        var start = DateTimeOffset.UtcNow;

        Assert.False(state.Update(null, settings, start, false));
        Assert.False(state.Update(null, settings, start.AddSeconds(1.9), false));
        Assert.True(state.Update(null, settings, start.AddSeconds(2), false));
        Assert.True(state.IsCollapsed);

        var audible = new SpectrumFrame([0.5f], start.AddSeconds(2.1), false);
        Assert.False(state.Update(audible, settings, start.AddSeconds(2.1), false));
        Assert.False(state.IsCollapsed);
    }

    [Fact]
    public void AutoCollapseIsDisabledInEditModeOrBySetting()
    {
        var start = DateTimeOffset.UtcNow;
        var state = new SpectrumAutoCollapseState();
        var settings = new SpectrumComponentSettings { SilenceCollapseDelaySeconds = 1 };

        Assert.False(state.Update(null, settings, start.AddSeconds(2), true));
        settings.AutoCollapseEnabled = false;
        Assert.False(state.Update(null, settings, start.AddSeconds(4), false));
        Assert.False(state.IsCollapsed);
    }

    [Fact]
    public void AutoCollapseUsesFinalDisplayedAmplitude()
    {
        var start = DateTimeOffset.UtcNow;
        var state = new SpectrumAutoCollapseState();
        var settings = new SpectrumComponentSettings
        {
            Amplitude = 0.25,
            SilenceCollapseDelaySeconds = 1
        };
        var barelyAudible = new SpectrumFrame([0.01f], start, false);

        Assert.False(state.Update(barelyAudible, settings, start, false));
        Assert.True(state.Update(barelyAudible, settings, start.AddSeconds(1), false));

        settings.Amplitude = 1;
        var visible = new SpectrumFrame([0.01f], start.AddSeconds(1.1), false);
        Assert.False(state.Update(visible, settings, start.AddSeconds(1.1), false));
    }

    [Fact]
    public void ReusableResampleAndLayoutBuffersMatchAllocatingHelpers()
    {
        var source = Enumerable.Range(0, 96).Select(index => index / 95f).ToArray();
        var resampled = new float[48];
        SpectrumBandResampler.ResampleInto(source, resampled, 1.5);
        Assert.Equal(SpectrumBandResampler.Resample(source, 48, 1.5), resampled);

        var expected = SpectrumBarLayout.Calculate(new Size(240, 50), resampled,
            SpectrumDisplayMode.BottomUp, new Thickness(1, 1, 1, 3));
        var actual = new Rect[48];
        SpectrumBarLayout.CalculateInto(new Size(240, 50), resampled,
            SpectrumDisplayMode.BottomUp, new Thickness(1, 1, 1, 3), actual);
        Assert.Equal(expected, actual);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void ComponentMetadataIsStable()
    {
        var info = typeof(SpectrumComponent).GetCustomAttribute<ComponentInfo>();
        Assert.NotNull(info);
        Assert.Equal(Guid.Parse("62E353BD-8D04-4BB1-8462-C1BC00497B7E"), info!.Guid);
        Assert.Equal("律动岛频谱", info.Name);
    }

    [Fact]
    public void RefreshControllerSubscribesOnceAndAlwaysUnsubscribes()
    {
        var clock = new FakeRenderClock();
        var invalidations = 0;
        using var controller = new SpectrumComponentRefreshController(clock, () => invalidations++, () => 60);
        controller.Attach();
        controller.Attach();
        Assert.True(controller.IsAttached);
        Assert.Equal(1, clock.SubscribeCount);
        clock.Tick();
        Assert.Equal(1, invalidations);
        controller.Detach();
        controller.Detach();
        Assert.False(controller.IsAttached);
        Assert.Equal(1, clock.UnsubscribeCount);
        Assert.Equal(60, clock.FrameRate);
    }

    [Fact]
    public void ComponentFrameRateDefaultsValidatesAndKeepsUnlimited()
    {
        var settings = new SpectrumComponentSettings();
        Assert.Equal(30, settings.FrameRate);

        settings.FrameRate = 120;
        Assert.Equal(120, settings.FrameRate);
        settings.FrameRate = 0;
        Assert.Equal(0, settings.FrameRate);
        settings.FrameRate = 144;
        Assert.Equal(30, settings.FrameRate);
    }

    [Theory]
    [InlineData(59.94, new[] { 30, 60, 0 })]
    [InlineData(60, new[] { 30, 60, 0 })]
    [InlineData(75, new[] { 30, 60, 0 })]
    [InlineData(90, new[] { 30, 60, 90, 0 })]
    [InlineData(119.88, new[] { 30, 60, 90, 120, 0 })]
    [InlineData(120, new[] { 30, 60, 90, 120, 0 })]
    public void FrameRateOptionsFollowDisplayCapability(double refreshRate, int[] expected)
    {
        Assert.Equal(expected, SpectrumFrameRateOptions.ForRefreshRate(refreshRate).Select(option => option.Value));
    }

    [Fact]
    public void UnknownDisplayRefreshRateFallsBackToThirtyAndSixty()
    {
        Assert.Equal([30, 60, 0], SpectrumFrameRateOptions.ForRefreshRate(null).Select(option => option.Value));
    }

    [Fact]
    public void SharedClockUsesHighestEffectiveRequestedRate()
    {
        Assert.Equal(TimeSpan.FromSeconds(1d / 30), SpectrumRenderClock.IntervalFor([30]));
        Assert.Equal(TimeSpan.FromSeconds(1d / 120), SpectrumRenderClock.IntervalFor([30, 120]));
        Assert.Equal(TimeSpan.FromSeconds(1d / 144), SpectrumRenderClock.IntervalFor([30, 144]));
    }

    [Fact]
    public void SharedClockDispatchesEachComponentAtItsOwnRate()
    {
        const long start = 1_000_000;
        var thirty = new SpectrumRenderClock.SubscriptionState(() => { }, () => 30)
        {
            NextDispatchTimestamp = start + Stopwatch.Frequency / 30
        };
        var oneTwenty = new SpectrumRenderClock.SubscriptionState(() => { }, () => 120)
        {
            NextDispatchTimestamp = start + Stopwatch.Frequency / 120
        };
        var higher = new SpectrumRenderClock.SubscriptionState(() => { }, () => 144)
        {
            NextDispatchTimestamp = start + Stopwatch.Frequency / 144
        };

        var afterTenMilliseconds = start + Stopwatch.Frequency / 100;
        Assert.False(SpectrumRenderClock.ShouldDispatch(thirty, afterTenMilliseconds));
        Assert.True(SpectrumRenderClock.ShouldDispatch(oneTwenty, afterTenMilliseconds));
        Assert.True(SpectrumRenderClock.ShouldDispatch(higher, afterTenMilliseconds));
    }

    [Fact]
    public void MixedSixtyAndSeventyFiveFpsScheduleTheNearestDeadline()
    {
        const long now = 1_000_000;
        var sixty = new SpectrumRenderClock.SubscriptionState(() => { }, () => 60)
        {
            EffectiveFrameRate = 60,
            NextDispatchTimestamp = now + Stopwatch.Frequency / 60
        };
        var seventyFive = new SpectrumRenderClock.SubscriptionState(() => { }, () => 75)
        {
            EffectiveFrameRate = 75,
            NextDispatchTimestamp = now + Stopwatch.Frequency / 75
        };

        var delay = SpectrumRenderClock.DelayUntilNextDispatch([sixty, seventyFive], now);

        Assert.Equal(1000d / 75d, delay.TotalMilliseconds, 2);
    }

    [Fact]
    public void HighFrameRateInterpolatesBetweenAnalysisFrames()
    {
        var interpolator = new SpectrumFrameInterpolator();
        var start = DateTimeOffset.UtcNow;
        var low = new SpectrumFrame([0f], start, false);
        var high = new SpectrumFrame([1f], start.AddMilliseconds(33), false);

        Assert.Equal(0, interpolator.Resolve(low, 1, 1, 0, start)[0]);
        Assert.InRange(interpolator.Resolve(high, 1, 1, 0, start.AddMilliseconds(33))[0], 0.80f, 0.81f);

        var middle = interpolator.Resolve(high, 1, 1, 0, start.AddMilliseconds(41))[0];
        Assert.InRange(middle, 0.89f, 0.91f);
        Assert.Equal(1, interpolator.Resolve(high, 1, 1, 0, start.AddMilliseconds(50))[0]);
    }

    [Fact]
    public void ThirtyFpsKeepsImmediateSpectrumResponse()
    {
        var interpolator = new SpectrumFrameInterpolator();
        var start = DateTimeOffset.UtcNow;
        interpolator.Resolve(new SpectrumFrame([0f], start, false), 1, 1, 30, start);

        var displayed = interpolator.Resolve(
            new SpectrumFrame([0.8f], start.AddMilliseconds(33), false),
            1,
            1,
            30,
            start.AddMilliseconds(33));

        Assert.Equal(0.8f, displayed[0], 5);
    }

    [Fact]
    public void SixtyFpsKeepsImmediateSpectrumResponse()
    {
        var interpolator = new SpectrumFrameInterpolator();
        var start = DateTimeOffset.UtcNow;
        interpolator.Resolve(new SpectrumFrame([0f], start, false), 1, 1, 60, start);

        var displayed = interpolator.Resolve(
            new SpectrumFrame([0.8f], start.AddMilliseconds(17), false), 1, 1, 60, start.AddMilliseconds(17));

        Assert.Equal(0.8f, displayed[0], 5);
    }

    [Fact]
    public void InterpolatorReusesBuffersAndResetClearsOldPicture()
    {
        var interpolator = new SpectrumFrameInterpolator();
        var now = DateTimeOffset.UtcNow;
        interpolator.Resolve(new SpectrumFrame(Enumerable.Repeat(1f, 96), now, false), 48, 1, 120, now);
        Assert.Equal(48, interpolator.BufferCapacity);

        interpolator.Reset();
        var cleared = interpolator.Resolve(new SpectrumFrame(new float[96], now.AddMilliseconds(33), false),
            48, 1, 120, now.AddMilliseconds(33));
        Assert.All(cleared, value => Assert.Equal(0, value));
        Assert.Equal(48, interpolator.BufferCapacity);
    }

    [Fact]
    public void SilentFrameImmediatelyClearsHighFrameRateTransition()
    {
        var interpolator = new SpectrumFrameInterpolator();
        var now = DateTimeOffset.UtcNow;
        interpolator.Resolve(new SpectrumFrame([1f], now, false), 1, 1, 120, now);

        var cleared = interpolator.Resolve(
            new SpectrumFrame([0f], now.AddMilliseconds(8), true), 1, 1, 120, now.AddMilliseconds(8));

        Assert.Equal(0, cleared[0]);
    }

    [Theory]
    [InlineData(5, 8)]
    [InlineData(16.6667, 15)]
    [InlineData(100, 17)]
    public void TransitionDurationStaysResponsive(double observedMilliseconds, double expectedMilliseconds)
    {
        var duration = SpectrumFrameInterpolator.CalculateTransitionDuration(
            TimeSpan.FromMilliseconds(observedMilliseconds));
        Assert.Equal(expectedMilliseconds, duration.TotalMilliseconds, 3);
    }

    [Theory]
    [InlineData(null, 60)]
    [InlineData(59.94, 60)]
    [InlineData(75d, 75)]
    [InlineData(119.88, 120)]
    [InlineData(144d, 144)]
    [InlineData(360d, 240)]
    public void HigherFrameRateFollowsDisplayAndStaysSafe(double? refreshRate, int expected)
    {
        Assert.Equal(expected, SpectrumFrameRateOptions.ResolveHigherFrameRate(refreshRate));
    }

    [Theory]
    [InlineData(90, 60, 60)]
    [InlineData(120, 75, 60)]
    [InlineData(90, 90, 90)]
    [InlineData(120, 144, 120)]
    [InlineData(60, 30, 60)]
    [InlineData(0, 75, 75)]
    public void FrameRatePolicyUsesDisplayCapability(int configured, double refreshRate, int expected)
    {
        Assert.Equal(expected, SpectrumFrameRatePolicy.ResolveEffectiveFrameRate(configured, refreshRate));
    }

    [Theory]
    [InlineData(90)]
    [InlineData(120)]
    public void UnknownRefreshRateDoesNotRewriteSavedHighFrameRate(int configured)
    {
        Assert.Equal(configured, SpectrumFrameRatePolicy.ResolvePersistedFrameRate(configured, null));
        Assert.Equal(60, SpectrumFrameRatePolicy.ResolveEffectiveFrameRate(configured, null));
    }

    [Fact]
    public void LatestFrameCanBeCleared()
    {
        var provider = new SpectrumFrameProvider();
        provider.Publish(new SpectrumFrame([0.5f], DateTimeOffset.UtcNow, false));
        Assert.NotNull(provider.Latest);
        provider.Clear();
        Assert.Null(provider.Latest);
    }

    private sealed class FakeRenderClock : ISpectrumRenderClock
    {
        private Action? _callback;
        public int SubscribeCount { get; private set; }
        public int UnsubscribeCount { get; private set; }
        public int FrameRate { get; private set; }
        public IDisposable Subscribe(Action callback, Func<int> frameRateProvider)
        {
            SubscribeCount++;
            FrameRate = frameRateProvider();
            _callback = callback;
            return new CallbackDisposable(() => { UnsubscribeCount++; _callback = null; });
        }
        public void Tick() => _callback?.Invoke();
    }

    private sealed class CallbackDisposable(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
