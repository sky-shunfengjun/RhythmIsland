using Avalonia;
using ClassIsland.Core.Assists;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using RhythmIsland.Abstractions;
using RhythmIsland.Models;
using RhythmIsland.Services;
using RhythmIsland.Theming.Shared;

namespace RhythmIsland.Controls.Components;

[ComponentInfo("62E353BD-8D04-4BB1-8462-C1BC00497B7E", "律动岛频谱", "\ueff7", "显示默认扬声器的实时频谱动画。")]
public partial class SpectrumComponent : ComponentBase<SpectrumComponentSettings>
{
    private readonly ISpectrumFrameProvider _frames;
    private readonly ISpectrumRenderClock _renderClock;
    private readonly ISystemMediaCoverService _mediaCoverService;
    private readonly SpectrumDisplayCapabilityService _displayCapabilities;
    private readonly SpectrumAutoCollapseState _autoCollapse = new();
    private SpectrumVisualHostController? _visualHost;

    public SpectrumComponent() : this(
        IAppHost.GetService<ISpectrumFrameProvider>(),
        IAppHost.GetService<ISpectrumRenderClock>(),
        IAppHost.GetService<ISystemMediaCoverService>(),
        IAppHost.GetService<SpectrumDisplayCapabilityService>())
    {
    }

    internal SpectrumComponent(
        ISpectrumFrameProvider frames,
        ISpectrumRenderClock renderClock,
        ISystemMediaCoverService mediaCoverService,
        SpectrumDisplayCapabilityService? displayCapabilities = null)
    {
        InitializeComponent();
        _frames = frames;
        _renderClock = renderClock;
        _mediaCoverService = mediaCoverService;
        _displayCapabilities = displayCapabilities ?? new SpectrumDisplayCapabilityService();
        BarsControl.Initialize(frames);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);
        _autoCollapse.Reset();
        IsVisible = true;
        _visualHost = new SpectrumVisualHostController(
            this, BarsControl, Settings, _renderClock, _mediaCoverService, _displayCapabilities, UpdateAutoCollapse);
        _visualHost.Attach();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        _visualHost?.Dispose();
        _visualHost = null;
        _autoCollapse.Reset();
        IsVisible = true;
        base.OnDetachedFromVisualTree(eventArgs);
    }

    private void UpdateAutoCollapse()
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
    }
}
