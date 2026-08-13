using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System.ComponentModel;
using ClassIsland.Core.Assists;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using RhythmIsland.Abstractions;
using RhythmIsland.Models;
using RhythmIsland.Services;

namespace RhythmIsland.Controls.Components;

[ComponentInfo("62E353BD-8D04-4BB1-8462-C1BC00497B7E", "律动岛频谱", "\ue768", "显示默认扬声器的实时频谱动画。")]
public partial class SpectrumComponent : ComponentBase<SpectrumComponentSettings>
{
    private readonly ISpectrumFrameProvider _frames;
    private readonly SpectrumComponentRefreshController _refreshController;
    private readonly ISystemMediaCoverService _mediaCoverService;
    private readonly SpectrumAutoCollapseState _autoCollapse = new();
    private long _displayRefreshCacheExpiresAt;
    private int _cachedHigherFrameRate = 60;
    private IDisposable? _mediaCoverLease;

    public SpectrumComponent() : this(
        IAppHost.GetService<ISpectrumFrameProvider>(),
        IAppHost.GetService<ISpectrumRenderClock>(),
        IAppHost.GetService<ISystemMediaCoverService>())
    {
    }

    internal SpectrumComponent(
        ISpectrumFrameProvider frames,
        ISpectrumRenderClock renderClock,
        ISystemMediaCoverService mediaCoverService)
    {
        InitializeComponent();
        _frames = frames;
        _mediaCoverService = mediaCoverService;
        BarsControl.Initialize(frames);
        _refreshController = new SpectrumComponentRefreshController(renderClock, OnRefreshTick,
            ResolveEffectiveFrameRate);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);
        BarsControl.SetComponentSettings(Settings);
        Settings.PropertyChanged += OnComponentSettingsChanged;
        _mediaCoverService.Changed += OnMediaCoverChanged;
        RefreshMediaCoverSubscription();
        _autoCollapse.Reset();
        _displayRefreshCacheExpiresAt = 0;
        IsVisible = true;
        _refreshController.Attach();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        _refreshController.Detach();
        Settings.PropertyChanged -= OnComponentSettingsChanged;
        _mediaCoverService.Changed -= OnMediaCoverChanged;
        _mediaCoverLease?.Dispose();
        _mediaCoverLease = null;
        BarsControl.SetMediaPalette(null);
        _autoCollapse.Reset();
        IsVisible = true;
        base.OnDetachedFromVisualTree(eventArgs);
    }

    private int ResolveEffectiveFrameRate()
    {
        if (Settings.FrameRate != 0) return Settings.FrameRate;

        var now = Environment.TickCount64;
        if (now < _displayRefreshCacheExpiresAt) return _cachedHigherFrameRate;

        _displayRefreshCacheExpiresAt = now + 1000;
        try
        {
            var screen = TopLevel.GetTopLevel(this)?.Screens?.ScreenFromVisual(this);
            var refreshRate = screen is null ? null : DisplayRefreshRateProvider.GetForBounds(screen.Bounds);
            _cachedHigherFrameRate = SpectrumFrameRateOptions.ResolveHigherFrameRate(refreshRate);
        }
        catch
        {
            _cachedHigherFrameRate = 60;
        }

        return _cachedHigherFrameRate;
    }

    private void OnRefreshTick()
    {
        var collapsed = _autoCollapse.Update(
            _frames.Latest,
            Settings,
            DateTimeOffset.UtcNow,
            MainWindowStylesAssist.GetMainWindowInEditMode(this));

        if (collapsed != !IsVisible)
        {
            IsVisible = !collapsed;
            InvalidateMeasure();
        }

        BarsControl.InvalidateVisual();
    }

    private void OnComponentSettingsChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(SpectrumComponentSettings.ColorSource))
            RefreshMediaCoverSubscription();
    }

    private void RefreshMediaCoverSubscription()
    {
        if (Settings.ColorSource == SpectrumColorSource.MediaCover)
        {
            _mediaCoverLease ??= _mediaCoverService.Acquire();
            ApplyMediaCoverState();
            return;
        }

        _mediaCoverLease?.Dispose();
        _mediaCoverLease = null;
        BarsControl.SetMediaPalette(null);
        Settings.SetMediaCoverStatusText("选择音乐封面后显示获取状态。");
        BarsControl.InvalidateVisual();
    }

    private void OnMediaCoverChanged(object? sender, EventArgs eventArgs)
    {
        if (Settings.ColorSource != SpectrumColorSource.MediaCover) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (Settings.ColorSource == SpectrumColorSource.MediaCover && _mediaCoverLease is not null)
                ApplyMediaCoverState();
        }, DispatcherPriority.Render);
    }

    private void ApplyMediaCoverState()
    {
        BarsControl.SetMediaPalette(_mediaCoverService.CurrentPalette);
        Settings.SetMediaCoverStatusText(_mediaCoverService.StatusText);
        BarsControl.InvalidateVisual();
    }
}
