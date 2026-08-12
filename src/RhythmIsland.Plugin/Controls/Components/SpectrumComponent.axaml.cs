using Avalonia;
using ClassIsland.Core.Assists;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using RhythmIsland.Abstractions;
using RhythmIsland.Models;
using RhythmIsland.Services;

namespace RhythmIsland.Controls.Components;

[ComponentInfo("62E353BD-8D04-4BB1-8462-C1BC00497B7E", "律动岛频谱", "\ue768", "显示默认扬声器的实时柱状频谱。")]
public partial class SpectrumComponent : ComponentBase<SpectrumComponentSettings>
{
    private readonly ISpectrumFrameProvider _frames;
    private readonly SpectrumComponentRefreshController _refreshController;
    private readonly SpectrumAutoCollapseState _autoCollapse = new();

    public SpectrumComponent() : this(
        IAppHost.GetService<ISpectrumFrameProvider>(),
        IAppHost.GetService<ISpectrumRenderClock>())
    {
    }

    internal SpectrumComponent(ISpectrumFrameProvider frames, ISpectrumRenderClock renderClock)
    {
        InitializeComponent();
        _frames = frames;
        BarsControl.Initialize(frames);
        _refreshController = new SpectrumComponentRefreshController(renderClock, OnRefreshTick);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);
        BarsControl.SetComponentSettings(Settings);
        _autoCollapse.Reset();
        IsVisible = true;
        _refreshController.Attach();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        _refreshController.Detach();
        _autoCollapse.Reset();
        IsVisible = true;
        base.OnDetachedFromVisualTree(eventArgs);
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
}
