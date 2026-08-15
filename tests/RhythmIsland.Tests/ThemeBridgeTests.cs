using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using RhythmIsland.Abstractions;
using RhythmIsland.Controls.Components;
using RhythmIsland.Models;
using RhythmIsland.Services;
using RhythmIsland.Theming;
using RhythmIsland.Theming.Background;
using RhythmIsland.Theming.Features;
using RhythmIsland.ViewModels;

namespace RhythmIsland.Tests;

[Collection("ThemeBridgeSerial")]
public sealed class ThemeBridgeTests
{
    [Fact]
    public void ThemeUsesMainLineSelectorAndStableBridgeWithoutReplacingTemplate()
    {
        var themePath = RepositoryPath("src", "RhythmIsland.Theme", "Styles.axaml");
        var source = File.ReadAllText(themePath);
        var root = XElement.Parse(source);
        Assert.Equal("Styles", root.Name.LocalName);
        Assert.DoesNotContain(root.Descendants(), element => element.Name.LocalName == "ControlTemplate");
        Assert.DoesNotContain("assembly=RhythmIsland", source, StringComparison.Ordinal);
        Assert.DoesNotContain("clr-namespace:RhythmIsland", source, StringComparison.Ordinal);

        var styles = root.Descendants().Where(element => element.Name.LocalName == "Style").ToArray();
        var outerStyle = styles.Single(element => (string?)element.Attribute("Selector") == "controls|MainWindowLine");
        var mainLineStyle = styles.Single(element => (string?)element.Attribute("Selector") == "controls|MainWindowLine[IsMainLine=True]");
        var backgroundStyle = styles.Single(element => ((string?)element.Attribute("Selector"))?.Contains("PART_GridWrapper") == true);
        Assert.Equal("controls|MainWindowLine", (string?)outerStyle.Attribute("Selector"));
        Assert.Contains(backgroundStyle, mainLineStyle.Descendants());
        Assert.Equal("^ /template/ Grid#PART_GridWrapper", (string?)backgroundStyle.Attribute("Selector"));
        Assert.Contains(outerStyle.Elements(), element =>
            (string?)element.Attribute("Property") == "Tag" &&
            (string?)element.Attribute("Value") == SpectrumThemeBridge.CurrentThemeMarker);
        Assert.Contains(mainLineStyle.Elements(), element =>
            (string?)element.Attribute("Property") == "ToolTip.Tip" &&
            ((string?)element.Attribute("Value"))?.Contains("未检测到律动岛插件") == true);
        Assert.Contains(mainLineStyle.Elements(), element =>
            (string?)element.Attribute("Property") == "ToolTip.IsOpen" &&
            (string?)element.Attribute("Value") == "True");
        Assert.Contains(backgroundStyle.Descendants(), element =>
            (string?)element.Attribute("Property") == "Tag" &&
            (string?)element.Attribute("Value") == SpectrumThemeBridge.CurrentBridgeMarker);
        Assert.DoesNotContain(backgroundStyle.Descendants(), element =>
            (string?)element.Attribute("Property") == "ClipToBounds");
    }

    [AvaloniaFact]
    public void ThemeStylesLoadThroughAvaloniaRuntimeCompiler()
    {
        var source = File.ReadAllText(RepositoryPath("src", "RhythmIsland.Theme", "Styles.axaml"));

        var loaded = AvaloniaRuntimeXamlLoader.Load(
            source,
            typeof(ClassIsland.Controls.MainWindowLine).Assembly);

        Assert.IsAssignableFrom<IStyle>(loaded);
        Assert.IsType<Styles>(loaded);
    }

    [Fact]
    public void PluginSourceDoesNotRegisterTheme()
    {
        var source = File.ReadAllText(RepositoryPath("src", "RhythmIsland.Plugin", "Plugin.cs"));
        Assert.DoesNotContain("AddXamlTheme", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPageExposesBackgroundSpectrumSwitch()
    {
        var root = XElement.Load(RepositoryPath(
            "src", "RhythmIsland.Plugin", "Views", "SettingsPages", "RhythmIslandSettingsPage.axaml"));
        var expander = root.Descendants().Single(element =>
            element.Name.LocalName == "SettingsExpander" &&
            (string?)element.Attribute("Header") == "背景频谱");
        var toggle = expander.Descendants().Single(element => element.Name.LocalName == "ToggleSwitch");
        Assert.Contains("Settings.BackgroundSpectrum.IsEnabled", (string?)toggle.Attribute("IsChecked"));
    }

    [AvaloniaFact]
    public void LegacyEmptyBorderAttachesOnlyOnceAndDetachesCleanly()
    {
        using var fixture = new HostFixture();
        var border = new Border();

        Assert.True(fixture.Host.AttachLegacy(border));
        var firstChild = border.Child;
        Assert.NotNull(firstChild);
        Assert.False(firstChild!.IsHitTestVisible);
        Assert.True(fixture.Host.AttachLegacy(border));
        Assert.Same(firstChild, border.Child);
        Assert.Equal(BackgroundThemeState.Displaying, fixture.Status.State);

        fixture.Host.DetachLegacy(border);
        Assert.Null(border.Child);
        Assert.Equal(BackgroundThemeState.NotEnabled, fixture.Status.State);
    }

    [AvaloniaFact]
    public void CurrentMarkerAttachesOnlyOnceAndMarkerRemovalDetaches()
    {
        using var fixture = new HostFixture();
        var original = new TextBlock { Text = "content" };
        var host = new Grid { Tag = SpectrumThemeBridge.CurrentBridgeMarker };
        host.Children.Add(original);

        SpectrumThemeBridge.ApplyMarker(host, fixture.Host);
        var firstChild = host.Children[0];
        SpectrumThemeBridge.ApplyMarker(host, fixture.Host);

        Assert.IsType<SpectrumBackgroundControl>(firstChild);
        Assert.Equal(2, host.Children.Count);
        Assert.Same(firstChild, host.Children[0]);
        Assert.Same(original, host.Children[1]);
        Assert.True(fixture.Host.IsMounted(host));

        host.Tag = null;
        SpectrumThemeBridge.ApplyMarker(host, fixture.Host);
        Assert.Single(host.Children);
        Assert.Same(original, host.Children[0]);
        Assert.False(fixture.Host.IsMounted(host));
    }

    [AvaloniaFact]
    public void LegacyV1MarkersRemainCompatibleAndRecommendThemeUpdate()
    {
        using var fixture = new HostFixture();
        var line = new Border { Tag = SpectrumThemeBridge.LegacyThemeMarker };
        var background = new Border { Tag = SpectrumThemeBridge.LegacyBridgeMarker };

        SpectrumThemeBridge.ApplyMarker(line, fixture.Host);
        SpectrumThemeBridge.ApplyMarker(background, fixture.Host);

        Assert.NotNull(background.Child);
        Assert.True(fixture.Host.IsMounted(background));
        Assert.Equal(BackgroundThemeState.Displaying, fixture.Status.State);
        Assert.Contains("主题版本较旧", fixture.Status.StatusText);
        Assert.Contains("独立透明度", fixture.Status.StatusText);
    }

    [AvaloniaFact]
    public void FixedWidthCentersWithoutChangingSavedWidthAndStretchModeFillsHost()
    {
        using var fixture = new HostFixture();
        var host = new Grid { Width = 600, Height = 40 };
        Assert.True(fixture.Host.Attach(host));
        var layer = Assert.IsType<SpectrumBackgroundControl>(host.Children[0]);
        var bars = Assert.IsType<SpectrumBarsControl>(layer.Children[1]);

        host.Measure(new Avalonia.Size(600, 40));
        host.Arrange(new Avalonia.Rect(0, 0, 600, 40));
        Assert.Equal(240, bars.Bounds.Width, 3);
        Assert.Equal(180, bars.Bounds.X, 3);

        host.Width = 180;
        host.Measure(new Avalonia.Size(180, 40));
        host.Arrange(new Avalonia.Rect(0, 0, 180, 40));
        Assert.Equal(240, bars.Bounds.Width, 3);
        Assert.Equal(-30, bars.Bounds.X, 3);
        Assert.Equal(240, fixture.Settings.BackgroundSpectrum.FixedWidth);

        fixture.Settings.BackgroundSpectrum.IsFixedWidthEnabled = false;
        host.Width = 600;
        host.Measure(new Avalonia.Size(600, 40));
        host.Arrange(new Avalonia.Rect(0, 0, 600, 40));
        Assert.Equal(600, bars.Bounds.Width, 3);
        Assert.Equal(0, bars.Bounds.X, 3);
    }

    [AvaloniaFact]
    public void FixedWidthOverlayDoesNotStretchTheMainLineAndClipsAtHostBounds()
    {
        using var fixture = new HostFixture();
        fixture.Settings.BackgroundSpectrum.FixedWidth = 600;
        var originalContent = new Border { Width = 180, Height = 40 };
        var host = new Grid();
        host.Children.Add(originalContent);

        Assert.True(fixture.Host.Attach(host));
        var layer = Assert.IsType<SpectrumBackgroundControl>(host.Children[0]);
        var bars = Assert.IsType<SpectrumBarsControl>(layer.Children[1]);

        host.Measure(new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(180, host.DesiredSize.Width, 3);
        Assert.Equal(0, layer.DesiredSize.Width, 3);

        host.Arrange(new Avalonia.Rect(0, 0, 180, 40));
        Assert.Equal(180, layer.Bounds.Width, 3);
        Assert.Equal(600, bars.Bounds.Width, 3);
        Assert.Equal(-210, bars.Bounds.X, 3);
        Assert.True(layer.ClipToBounds);
    }

    [AvaloniaFact]
    public void V2SpectrumLayerIsSiblingOfTransparentClassIslandBackground()
    {
        using var fixture = new HostFixture();
        var root = new Grid();
        var classIslandBackground = new Border { Opacity = 0 };
        var contentHost = new Grid();
        var originalContent = new TextBlock { Text = "content" };
        contentHost.Children.Add(originalContent);
        root.Children.Add(classIslandBackground);
        root.Children.Add(contentHost);

        Assert.True(fixture.Host.Attach(contentHost));

        var spectrumLayer = Assert.IsType<SpectrumBackgroundControl>(contentHost.Children[0]);
        Assert.Same(contentHost, spectrumLayer.Parent);
        Assert.NotSame(classIslandBackground, spectrumLayer.Parent);
        Assert.Equal(0, classIslandBackground.Opacity);
        Assert.Equal(1, spectrumLayer.Opacity);
        Assert.Same(originalContent, contentHost.Children[1]);
    }

    [AvaloniaFact]
    public void ThemePresenceAndBackgroundSwitchProduceDistinctStates()
    {
        using var fixture = new HostFixture();
        var line = new Border { Tag = SpectrumThemeBridge.CurrentThemeMarker };
        SpectrumThemeBridge.ApplyMarker(line, fixture.Host);
        Assert.Equal(BackgroundThemeState.EnabledWaiting, fixture.Status.State);

        var background = new Grid { Tag = SpectrumThemeBridge.CurrentBridgeMarker };
        SpectrumThemeBridge.ApplyMarker(background, fixture.Host);
        Assert.Equal(BackgroundThemeState.Displaying, fixture.Status.State);

        fixture.Settings.BackgroundSpectrum.IsEnabled = false;
        Assert.Equal(BackgroundThemeState.DisabledByUser, fixture.Status.State);
        Assert.False(background.Children[0].IsVisible);

        fixture.Settings.BackgroundSpectrum.IsEnabled = true;
        Assert.Equal(BackgroundThemeState.Displaying, fixture.Status.State);
        Assert.True(background.Children[0].IsVisible);
    }

    [AvaloniaFact]
    public void PluginHidesTheThemeOnlyErrorAndRestoresItWhenRemoved()
    {
        using var fixture = new HostFixture();
        var line = new Border { Tag = SpectrumThemeBridge.CurrentThemeMarker };
        using var tipStyle = line.SetValue(
            ToolTip.TipProperty,
            "律动岛背景主题错误：未检测到律动岛插件，请先安装并启用插件。",
            BindingPriority.Style);
        using var openStyle = line.SetValue(ToolTip.IsOpenProperty, true, BindingPriority.Style);

        SpectrumThemeBridge.ApplyMarker(line, fixture.Host);
        Assert.Null(line.GetValue(ToolTip.TipProperty));
        Assert.False(line.GetValue(ToolTip.IsOpenProperty));

        line.Tag = null;
        SpectrumThemeBridge.ApplyMarker(line, fixture.Host);
        Assert.Contains("未检测到律动岛插件", line.GetValue(ToolTip.TipProperty)?.ToString());
        Assert.True(line.GetValue(ToolTip.IsOpenProperty));
    }

    [AvaloniaFact]
    public void RegisteredTagHandlerAutomaticallyProcessesBridgeMarker()
    {
        using var fixture = new HostFixture();
        SpectrumThemeBridge.Register();
        SpectrumThemeBridge.Bind(fixture.Host);
        try
        {
            var host = new Grid { Tag = SpectrumThemeBridge.CurrentBridgeMarker };
            Assert.True(fixture.Host.IsMounted(host));
            Assert.NotEmpty(host.Children);
        }
        finally
        {
            SpectrumThemeBridge.Unbind(fixture.Host);
        }
    }

    [AvaloniaFact]
    public void RegisteredTagHandlerAutomaticallyClearsRemovedErrorMarker()
    {
        using var fixture = new HostFixture();
        SpectrumThemeBridge.Register();
        SpectrumThemeBridge.Bind(fixture.Host);
        try
        {
            var host = new Grid { Tag = SpectrumThemeBridge.BridgeMarkerPrefix + "v99" };
            Assert.Equal(BackgroundThemeState.ContractMismatch, fixture.Status.State);

            host.Tag = null;

            Assert.Equal(BackgroundThemeState.NotEnabled, fixture.Status.State);
        }
        finally
        {
            SpectrumThemeBridge.Unbind(fixture.Host);
        }
    }

    [AvaloniaFact]
    public void MarkerReceivedBeforeHostBindingIsReplayedAfterBinding()
    {
        SpectrumThemeBridge.Register();
        var host = new Grid { Tag = SpectrumThemeBridge.CurrentBridgeMarker };
        using var fixture = new HostFixture();

        Assert.False(fixture.Host.IsMounted(host));
        SpectrumThemeBridge.Bind(fixture.Host);
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(fixture.Host.IsMounted(host));
            Assert.NotEmpty(host.Children);
        }
        finally
        {
            SpectrumThemeBridge.Unbind(fixture.Host);
        }
    }

    [AvaloniaFact]
    public void TemplateReplacementKeepsNewMountAndFinalRemovalClearsStatus()
    {
        using var fixture = new HostFixture();
        var line = new Border { Tag = SpectrumThemeBridge.CurrentThemeMarker };
        SpectrumThemeBridge.ApplyMarker(line, fixture.Host);
        var oldBackground = new Grid { Tag = SpectrumThemeBridge.CurrentBridgeMarker };
        var newBackground = new Grid { Tag = SpectrumThemeBridge.CurrentBridgeMarker };

        SpectrumThemeBridge.ApplyMarker(oldBackground, fixture.Host);
        SpectrumThemeBridge.ApplyMarker(newBackground, fixture.Host);
        fixture.Host.Detach(oldBackground);

        Assert.True(fixture.Host.IsMounted(newBackground));
        Assert.Equal(BackgroundThemeState.Displaying, fixture.Status.State);

        fixture.Host.Detach(newBackground);
        Assert.Equal(BackgroundThemeState.EnabledWaiting, fixture.Status.State);
        line.Tag = null;
        SpectrumThemeBridge.ApplyMarker(line, fixture.Host);
        Assert.Equal(BackgroundThemeState.NotEnabled, fixture.Status.State);
    }

    [Fact]
    public void OpenSettingsViewModelObservesLiveThemeStatusChanges()
    {
        var status = new BackgroundThemeStatus();
        var changed = new List<string?>();
        status.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        var viewModel = new RhythmIslandSettingsPageViewModel(
            new RhythmIslandSettings(),
            new RuntimeStatus(),
            new FakeRuntimeService(),
            status);

        status.Set(BackgroundThemeState.EnabledWaiting, "配套主题已启用，正在等待主要行背景。");
        status.Set(BackgroundThemeState.Displaying, "配套背景主题正在主要行显示频谱。");

        Assert.Same(status, viewModel.BackgroundThemeStatus);
        Assert.Equal(BackgroundThemeState.Displaying, viewModel.BackgroundThemeStatus.State);
        Assert.Contains(nameof(BackgroundThemeStatus.State), changed);
        Assert.Contains(nameof(BackgroundThemeStatus.StatusText), changed);
    }

    [Fact]
    public void EnabledThemeWithSwitchOffReportsDisabledEvenBeforeMount()
    {
        using var fixture = new HostFixture();
        fixture.Settings.BackgroundSpectrum.IsEnabled = false;
        var line = new Border { Tag = SpectrumThemeBridge.CurrentThemeMarker };

        SpectrumThemeBridge.ApplyMarker(line, fixture.Host);

        Assert.Equal(BackgroundThemeState.DisabledByUser, fixture.Status.State);
        Assert.Equal("配套主题已启用，背景频谱已关闭。", fixture.Status.StatusText);
    }

    [AvaloniaFact]
    public void UnrelatedTagIsIgnored()
    {
        using var fixture = new HostFixture();
        var originalStatus = fixture.Status.StatusText;
        var border = new Border { Tag = "another-feature" };

        SpectrumThemeBridge.ApplyMarker(border, fixture.Host);

        Assert.Null(border.Child);
        Assert.False(fixture.Host.IsMounted(border));
        Assert.Equal(originalStatus, fixture.Status.StatusText);
    }

    [AvaloniaFact]
    public void UnknownMarkerVersionDetachesAndProducesReadableStatus()
    {
        using var fixture = new HostFixture();
        var host = new Grid { Tag = SpectrumThemeBridge.CurrentBridgeMarker };
        SpectrumThemeBridge.ApplyMarker(host, fixture.Host);
        host.Tag = SpectrumThemeBridge.BridgeMarkerPrefix + "v99";

        SpectrumThemeBridge.ApplyMarker(host, fixture.Host);

        Assert.Empty(host.Children);
        Assert.False(fixture.Host.IsMounted(host));
        Assert.Equal(BackgroundThemeState.ContractMismatch, fixture.Status.State);
        Assert.Contains("v99", fixture.Status.StatusText);
        Assert.Contains(SpectrumThemeBridge.CurrentContractVersion.ToString(), fixture.Status.StatusText);
    }

    [AvaloniaFact]
    public void RemovingUnknownMarkerClearsStaleContractError()
    {
        using var fixture = new HostFixture();
        var host = new Grid { Tag = SpectrumThemeBridge.BridgeMarkerPrefix + "v99" };
        SpectrumThemeBridge.ApplyMarker(host, fixture.Host);
        Assert.Equal(BackgroundThemeState.ContractMismatch, fixture.Status.State);

        host.Tag = null;
        SpectrumThemeBridge.ApplyMarker(host, fixture.Host);

        Assert.Equal(BackgroundThemeState.NotEnabled, fixture.Status.State);
    }

    [AvaloniaFact]
    public void OccupiedThirdPartyBackgroundIsNotOverwritten()
    {
        using var fixture = new HostFixture();
        var original = new TextBlock { Text = "third-party" };
        var border = new Border { Child = original };

        Assert.False(fixture.Host.AttachLegacy(border));
        Assert.Same(original, border.Child);
        Assert.Equal(BackgroundThemeState.Incompatible, fixture.Status.State);
    }

    [AvaloniaFact]
    public void RemovingIncompatibleTargetClearsStaleStructureError()
    {
        using var fixture = new HostFixture();
        var original = new TextBlock { Text = "third-party" };
        var border = new Border { Child = original };
        Assert.False(fixture.Host.AttachLegacy(border));

        fixture.Host.RemoveControl(border);

        Assert.Same(original, border.Child);
        Assert.Equal(BackgroundThemeState.NotEnabled, fixture.Status.State);
    }

    [AvaloniaFact]
    public void MultipleProblemTargetsAreAggregatedAndSuccessfulMountHasPriority()
    {
        using var fixture = new HostFixture();
        var mismatch = new Grid();
        var incompatible = new Border();
        fixture.Host.ReportContractMismatch(mismatch, "v99");
        fixture.Host.ReportIncompatible(incompatible, "被占用");
        Assert.Equal(BackgroundThemeState.ContractMismatch, fixture.Status.State);

        fixture.Host.RemoveControl(mismatch);
        Assert.Equal(BackgroundThemeState.Incompatible, fixture.Status.State);

        var current = new Grid();
        Assert.True(fixture.Host.Attach(current));
        Assert.Equal(BackgroundThemeState.Displaying, fixture.Status.State);

        fixture.Host.Detach(current);
        Assert.Equal(BackgroundThemeState.Incompatible, fixture.Status.State);
        fixture.Host.RemoveControl(incompatible);
        Assert.Equal(BackgroundThemeState.NotEnabled, fixture.Status.State);
    }

    [AvaloniaFact]
    public void BackgroundSpectrumFollowsLiveThemeAccentResource()
    {
        using var fixture = new HostFixture();
        var host = new Grid();
        host.Resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(Colors.OrangeRed);
        Assert.True(fixture.Host.Attach(host));
        var window = new Window { Content = host };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var layer = Assert.IsType<SpectrumBackgroundControl>(host.Children[0]);
        var bars = Assert.IsType<SpectrumBarsControl>(layer.Children[1]);

        Assert.Equal(Colors.OrangeRed, Assert.IsAssignableFrom<ISolidColorBrush>(bars.BarBrush).Color);

        host.Resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(Colors.MediumPurple);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Colors.MediumPurple, Assert.IsAssignableFrom<ISolidColorBrush>(bars.BarBrush).Color);
        window.Close();
    }

    [AvaloniaFact]
    public void BrokenFeatureDoesNotPreventBaseSpectrumFromMounting()
    {
        using var fixture = new HostFixture([new BrokenFeature()]);
        var host = new Grid();
        Assert.True(fixture.Host.Attach(host));
        Assert.NotEmpty(host.Children);
    }

    [Fact]
    public void ContractMismatchProducesReadableStatus()
    {
        using var fixture = new HostFixture();
        fixture.Host.ReportContractMismatch(new Border(), "99");
        Assert.Equal(BackgroundThemeState.ContractMismatch, fixture.Status.State);
        Assert.Contains("99", fixture.Status.StatusText);
        Assert.Contains(SpectrumThemeBridge.CurrentContractVersion.ToString(), fixture.Status.StatusText);
    }

    private static string RepositoryPath(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return Path.Combine([root, .. parts]);
    }

    private sealed class HostFixture : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "RhythmIsland.Theme.Tests", Guid.NewGuid().ToString("N"));
        private readonly RhythmIslandSettingsStore _store;

        internal HostFixture(IEnumerable<IBackgroundThemeFeature>? features = null)
        {
            _store = new RhythmIslandSettingsStore(_folder, NullLogger<RhythmIslandSettingsStore>.Instance);
            Status = new BackgroundThemeStatus();
            Host = new SpectrumBackgroundHostService(
                _store,
                new SpectrumFrameProvider(),
                new FakeClock(),
                new FakeMediaCoverService(),
                new SpectrumDisplayCapabilityService(),
                features ?? [],
                Status,
                NullLogger<SpectrumBackgroundHostService>.Instance);
        }

        internal BackgroundThemeStatus Status { get; }
        internal SpectrumBackgroundHostService Host { get; }
        internal RhythmIslandSettings Settings => _store.Settings;

        public void Dispose()
        {
            _store.Dispose();
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }
    }

    private sealed class FakeClock : ISpectrumRenderClock
    {
        public IDisposable Subscribe(Action callback, Func<int> frameRateProvider) => new Disposable();
    }

    private sealed class FakeMediaCoverService : ISystemMediaCoverService
    {
        public event EventHandler? Changed { add { } remove { } }
        public SpectrumPalette? CurrentPalette => null;
        public SystemMediaCoverStatus Status => SystemMediaCoverStatus.Unavailable;
        public string StatusText => "不可用";
        public IDisposable Acquire() => new Disposable();
    }

    private sealed class Disposable : IDisposable { public void Dispose() { } }

    private sealed class FakeRuntimeService : IRhythmIslandRuntimeService
    {
        public Task ApplyEnabledStateAsync(bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> RestartCaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class BrokenFeature : IBackgroundThemeFeature
    {
        public string Id => "broken";
        public BackgroundThemeFeatureLayer Layer => BackgroundThemeFeatureLayer.AboveSpectrum;
        public Control CreateControl(BackgroundThemeFeatureContext context) => throw new InvalidOperationException("test");
    }
}

[CollectionDefinition("ThemeBridgeSerial", DisableParallelization = true)]
public sealed class ThemeBridgeSerialCollection;
