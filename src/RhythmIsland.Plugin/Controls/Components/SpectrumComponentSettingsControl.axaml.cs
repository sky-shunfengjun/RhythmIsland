using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ClassIsland.Core.Abstractions.Controls;
using RhythmIsland.Models;
using RhythmIsland.Services;

namespace RhythmIsland.Controls.Components;

public partial class SpectrumComponentSettingsControl : ComponentBase<SpectrumComponentSettings>
{
    public SpectrumComponentSettingsControl() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);
        RefreshFrameRateOptions();
    }

    private void RefreshFrameRateOptions()
    {
        double? refreshRate = null;
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            var screens = topLevel?.Screens;
            var screen = screens?.ScreenFromVisual(this);
            if (screen is not null) refreshRate = DisplayRefreshRateProvider.GetForBounds(screen.Bounds);
        }
        catch (Exception)
        {
            // 部分平台或 Headless 环境不会提供刷新率，保守显示到 60 FPS。
        }

        var options = SpectrumFrameRateOptions.ForRefreshRate(refreshRate);
        if (refreshRate is null && Settings.FrameRate is 90 or 120 &&
            options.All(option => option.Value != Settings.FrameRate))
        {
            options = options
                .Append(new SpectrumFrameRateOption(Settings.FrameRate, $"{Settings.FrameRate} FPS（暂时无法检测）"))
                .OrderBy(option => option.Value == 0 ? int.MaxValue : option.Value)
                .ToArray();
        }
        Settings.SetAvailableFrameRates(options);
        var persisted = SpectrumFrameRatePolicy.ResolvePersistedFrameRate(Settings.FrameRate, refreshRate);
        if (persisted != Settings.FrameRate) Settings.FrameRate = persisted;
    }
}
