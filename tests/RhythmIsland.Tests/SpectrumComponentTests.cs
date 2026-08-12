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
            DisplayMode = (SpectrumDisplayMode)999,
            UseCustomColor = true,
            CustomColor = Colors.Orange
        };
        Assert.Equal(SpectrumDisplayMode.BottomUp, settings.DisplayMode);
        Assert.True(settings.UseCustomColor);
        Assert.Equal(Colors.Orange, settings.CustomColor);
    }

    [Fact]
    public void ComponentColorSurvivesJsonRoundTrip()
    {
        var source = new SpectrumComponentSettings
        {
            UseCustomColor = true,
            CustomColor = Color.Parse("#FF12ABEF"),
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
        Assert.True(restored!.UseCustomColor);
        Assert.Equal(source.CustomColor, restored.CustomColor);
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
            Width = double.PositiveInfinity,
            SilenceCollapseDelaySeconds = double.NaN
        };
        Assert.Equal(48, settings.BarCount);
        Assert.Equal(1, settings.Amplitude);
        Assert.Equal(0.65, settings.Opacity);
        Assert.Equal(240, settings.Width);
        Assert.True(settings.AutoCollapseEnabled);
        Assert.Equal(5, settings.SilenceCollapseDelaySeconds);
        settings.Width = 10;
        settings.Amplitude = 8;
        settings.Opacity = 5;
        Assert.Equal(60, settings.Width);
        Assert.Equal(3, settings.Amplitude);
        Assert.Equal(1, settings.Opacity);
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
        var settings = new SpectrumComponentSettings { CustomColorText = "invalid" };
        Assert.Equal(Color.Parse("#FF4DA3FF"), settings.CustomColor);
        Assert.Equal("#FF4DA3FF", settings.CustomColorText);
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
        using var controller = new SpectrumComponentRefreshController(clock, () => invalidations++);
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
        public IDisposable Subscribe(Action callback)
        {
            SubscribeCount++;
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
