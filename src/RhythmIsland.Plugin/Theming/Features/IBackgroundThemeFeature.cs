using Avalonia;
using Avalonia.Controls;
using RhythmIsland.Abstractions;
using RhythmIsland.Models;

namespace RhythmIsland.Theming.Features;

internal enum BackgroundThemeFeatureLayer
{
    BehindSpectrum,
    AboveSpectrum
}

internal sealed record BackgroundThemeFeatureContext(
    ISpectrumFrameProvider Frames,
    ISpectrumRenderClock RenderClock,
    SpectrumBackgroundSettings Settings,
    Func<Size> BackgroundSize,
    Func<SpectrumPalette?> FinalPalette);

/// <summary>
/// 未来主题专属动态功能的受限入口。实现不能获得原始音频或主窗口视觉树。
/// </summary>
internal interface IBackgroundThemeFeature
{
    string Id { get; }
    BackgroundThemeFeatureLayer Layer { get; }
    Control CreateControl(BackgroundThemeFeatureContext context);
}
