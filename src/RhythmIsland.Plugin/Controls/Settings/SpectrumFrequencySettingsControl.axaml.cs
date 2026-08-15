using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassIsland.Shared;
using System.ComponentModel;
using RhythmIsland.Models;
using RhythmIsland.Services;

namespace RhythmIsland.Controls.Settings;

public partial class SpectrumFrequencySettingsControl : UserControl
{
    public static readonly StyledProperty<SpectrumVisualSettings?> SettingsProperty =
        AvaloniaProperty.Register<SpectrumFrequencySettingsControl, SpectrumVisualSettings?>(nameof(Settings));

    private SpectrumDisplayCapabilityService? _displayCapabilities;
    private bool _attached;

    public SpectrumFrequencySettingsControl() => InitializeComponent();

    public SpectrumVisualSettings? Settings
    {
        get => GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SettingsProperty) return;
        if (_attached && change.OldValue is SpectrumVisualSettings oldSettings)
            oldSettings.PropertyChanged -= OnSettingsChanged;
        if (_attached && change.NewValue is SpectrumVisualSettings newSettings)
            newSettings.PropertyChanged += OnSettingsChanged;
        if (_attached) RefreshFrameRateOptions();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);
        _attached = true;
        if (Settings is not null) Settings.PropertyChanged += OnSettingsChanged;
        _displayCapabilities = IAppHost.TryGetService<SpectrumDisplayCapabilityService>();
        if (_displayCapabilities is not null) _displayCapabilities.Changed += OnDisplayCapabilityChanged;
        RefreshFrameRateOptions();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        _attached = false;
        if (Settings is not null) Settings.PropertyChanged -= OnSettingsChanged;
        if (_displayCapabilities is not null) _displayCapabilities.Changed -= OnDisplayCapabilityChanged;
        _displayCapabilities = null;
        base.OnDetachedFromVisualTree(eventArgs);
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(SpectrumVisualSettings.FrameRate)) RefreshFrameRateOptions();
    }

    private void OnDisplayCapabilityChanged(object? sender, SpectrumDisplayCapabilityChangedEventArgs eventArgs)
    {
        if (!ReferenceEquals(eventArgs.Settings, Settings)) return;
        if (Dispatcher.UIThread.CheckAccess()) RefreshFrameRateOptions();
        else Dispatcher.UIThread.Post(RefreshFrameRateOptions);
    }

    private void RefreshFrameRateOptions()
    {
        var settings = Settings;
        if (settings is null) return;

        var refreshRate = _displayCapabilities?.GetRefreshRate(settings);
        var options = SpectrumFrameRateOptions.ForRefreshRate(refreshRate);
        if (refreshRate is null && settings.FrameRate is 90 or 120 &&
            options.All(option => option.Value != settings.FrameRate))
        {
            options = options
                .Append(new SpectrumFrameRateOption(settings.FrameRate, $"{settings.FrameRate} FPS（暂时无法检测）"))
                .OrderBy(option => option.Value == 0 ? int.MaxValue : option.Value)
                .ToArray();
        }

        settings.SetAvailableFrameRates(options);
        var persisted = SpectrumFrameRatePolicy.ResolvePersistedFrameRate(settings.FrameRate, refreshRate);
        if (persisted != settings.FrameRate) settings.FrameRate = persisted;
    }
}
