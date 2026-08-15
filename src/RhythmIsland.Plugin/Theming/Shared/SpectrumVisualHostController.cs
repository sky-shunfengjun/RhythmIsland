using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using RhythmIsland.Abstractions;
using RhythmIsland.Controls.Components;
using RhythmIsland.Models;
using RhythmIsland.Services;

namespace RhythmIsland.Theming.Shared;

/// <summary>
/// 组件与背景共用的视觉生命周期：刷新率、封面租约、调色状态和释放。
/// </summary>
internal sealed class SpectrumVisualHostController : IDisposable
{
    private readonly Control _owner;
    private readonly SpectrumBarsControl _bars;
    private readonly SpectrumVisualSettings _settings;
    private readonly ISystemMediaCoverService _mediaCoverService;
    private readonly SpectrumDisplayCapabilityService _displayCapabilities;
    private readonly SpectrumComponentRefreshController _refreshController;
    private readonly Action? _beforeInvalidate;
    private SpectrumDisplayCapabilityService.SpectrumDisplayCapabilityLease? _displayCapabilityLease;
    private IDisposable? _mediaCoverLease;
    private bool _attached;

    internal SpectrumVisualHostController(
        Control owner,
        SpectrumBarsControl bars,
        SpectrumVisualSettings settings,
        ISpectrumRenderClock renderClock,
        ISystemMediaCoverService mediaCoverService,
        SpectrumDisplayCapabilityService displayCapabilities,
        Action? beforeInvalidate = null)
    {
        _owner = owner;
        _bars = bars;
        _settings = settings;
        _mediaCoverService = mediaCoverService;
        _displayCapabilities = displayCapabilities;
        _beforeInvalidate = beforeInvalidate;
        _refreshController = new SpectrumComponentRefreshController(renderClock, OnRefreshTick, ResolveEffectiveFrameRate);
    }

    internal void Attach()
    {
        if (_attached) return;
        _attached = true;
        _bars.SetVisualSettings(_settings);
        _settings.PropertyChanged += OnSettingsChanged;
        _mediaCoverService.Changed += OnMediaCoverChanged;
        RefreshMediaCoverSubscription();
        _displayCapabilityLease = _displayCapabilities.Register(_settings, _owner);
        _refreshController.Attach();
    }

    internal void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _refreshController.Detach();
        _settings.PropertyChanged -= OnSettingsChanged;
        _mediaCoverService.Changed -= OnMediaCoverChanged;
        _mediaCoverLease?.Dispose();
        _mediaCoverLease = null;
        _displayCapabilityLease?.Dispose();
        _displayCapabilityLease = null;
        _bars.SetMediaPalette(null);
    }

    private int ResolveEffectiveFrameRate()
    {
        var refreshRate = _displayCapabilityLease?.GetRefreshRate();
        var persisted = SpectrumFrameRatePolicy.ResolvePersistedFrameRate(_settings.FrameRate, refreshRate);
        if (persisted != _settings.FrameRate) _settings.FrameRate = persisted;
        return SpectrumFrameRatePolicy.ResolveEffectiveFrameRate(_settings.FrameRate, refreshRate);
    }

    private void OnRefreshTick()
    {
        _bars.SetEffectiveFrameRate(ResolveEffectiveFrameRate());
        _beforeInvalidate?.Invoke();
        _bars.InvalidateVisual();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(SpectrumVisualSettings.ColorSource))
            RefreshMediaCoverSubscription();
        _bars.InvalidateVisual();
    }

    private void RefreshMediaCoverSubscription()
    {
        if (_settings.ColorSource == SpectrumColorSource.MediaCover)
        {
            _mediaCoverLease ??= _mediaCoverService.Acquire();
            ApplyMediaCoverState();
            return;
        }

        _mediaCoverLease?.Dispose();
        _mediaCoverLease = null;
        _bars.SetMediaPalette(null);
        _settings.SetMediaCoverStatusText("选择音乐封面后显示获取状态。");
        _bars.InvalidateVisual();
    }

    private void OnMediaCoverChanged(object? sender, EventArgs eventArgs)
    {
        if (_settings.ColorSource != SpectrumColorSource.MediaCover) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_attached && _settings.ColorSource == SpectrumColorSource.MediaCover && _mediaCoverLease is not null)
                ApplyMediaCoverState();
        }, DispatcherPriority.Render);
    }

    private void ApplyMediaCoverState()
    {
        _bars.SetMediaPalette(_mediaCoverService.CurrentPalette);
        _settings.SetMediaCoverStatusText(_mediaCoverService.StatusText);
        _bars.InvalidateVisual();
    }

    public void Dispose() => Detach();
}
