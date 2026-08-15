using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using RhythmIsland.Abstractions;
using RhythmIsland.Controls.Components;
using RhythmIsland.Models;
using RhythmIsland.Services;
using RhythmIsland.Theming.Features;
using RhythmIsland.Theming.Shared;

namespace RhythmIsland.Theming.Background;

internal sealed class SpectrumBackgroundControl : Grid
{
    private readonly SpectrumBarsControl _bars = new();
    private readonly SpectrumBackgroundSettings _settings;
    private readonly SpectrumVisualHostController _visualHost;
    private readonly List<IDisposable> _featureDisposables = [];
    private readonly IDisposable _accentBrushBinding;
    private bool _enabled;
    private bool _attachedToVisualTree;

    internal SpectrumBackgroundControl(
        SpectrumBackgroundSettings settings,
        ISpectrumFrameProvider frames,
        ISpectrumRenderClock renderClock,
        ISystemMediaCoverService mediaCoverService,
        SpectrumDisplayCapabilityService displayCapabilities,
        IEnumerable<IBackgroundThemeFeature> features,
        ILogger logger)
    {
        _settings = settings;
        IsHitTestVisible = false;
        ClipToBounds = true;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        _enabled = settings.IsEnabled;
        IsVisible = _enabled;

        var behindFeatures = new Grid { IsHitTestVisible = false };
        var aboveFeatures = new Grid { IsHitTestVisible = false };
        _bars.IsHitTestVisible = false;
        _bars.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        _bars.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        _accentBrushBinding = _bars.Bind(
            SpectrumBarsControl.BarBrushProperty,
            this.GetResourceObservable("AccentFillColorDefaultBrush", value => value as IBrush),
            BindingPriority.LocalValue);
        _bars.Initialize(frames);
        _bars.SetVisualSettings(settings);
        ApplyWidthSettings();
        _settings.PropertyChanged += OnSettingsChanged;

        Children.Add(behindFeatures);
        Children.Add(_bars);
        Children.Add(aboveFeatures);

        var featureContext = new BackgroundThemeFeatureContext(
            frames, renderClock, settings, () => Bounds.Size, () => _bars.LastResolvedPalette);
        foreach (var feature in features)
        {
            try
            {
                var control = feature.CreateControl(featureContext);
                control.IsHitTestVisible = false;
                (feature.Layer == BackgroundThemeFeatureLayer.BehindSpectrum ? behindFeatures : aboveFeatures)
                    .Children.Add(control);
                if (control is IDisposable disposable) _featureDisposables.Add(disposable);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "律动岛背景功能 {FeatureId} 启动失败，已仅停用该功能。", feature.Id);
            }
        }

        _visualHost = new SpectrumVisualHostController(
            this, _bars, settings, renderClock, mediaCoverService, displayCapabilities);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);
        _attachedToVisualTree = true;
        if (_enabled) _visualHost.Attach();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // This control is an overlay. Measure its children so they can render at the
        // requested fixed width, but never let that width affect the main line size.
        _ = base.MeasureOverride(availableSize);
        return new Size(0, 0);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        _attachedToVisualTree = false;
        _visualHost.Detach();
        base.OnDetachedFromVisualTree(eventArgs);
    }

    internal void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        IsVisible = enabled;
        if (!enabled)
        {
            _visualHost.Detach();
            return;
        }

        if (_attachedToVisualTree) _visualHost.Attach();
        _bars.InvalidateVisual();
    }

    internal void Release()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        _visualHost.Dispose();
        _accentBrushBinding.Dispose();
        foreach (var disposable in _featureDisposables) disposable.Dispose();
        _featureDisposables.Clear();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is not nameof(SpectrumBackgroundSettings.IsFixedWidthEnabled)
            and not nameof(SpectrumBackgroundSettings.FixedWidth)) return;
        ApplyWidthSettings();
    }

    private void ApplyWidthSettings()
    {
        if (_settings.IsFixedWidthEnabled)
        {
            _bars.Width = _settings.FixedWidth;
            _bars.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        }
        else
        {
            _bars.Width = double.NaN;
            _bars.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        }

        _bars.InvalidateMeasure();
        _bars.InvalidateVisual();
    }
}
