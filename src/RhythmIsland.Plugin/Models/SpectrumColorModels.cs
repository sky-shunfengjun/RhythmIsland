using Avalonia.Media;

namespace RhythmIsland.Models;

public enum SpectrumColorSource
{
    ThemeAccent,
    MediaCover,
    Custom
}

public enum SpectrumGradientMode
{
    Off,
    Static,
    Dynamic
}

public enum SpectrumGradientSpeed
{
    Slow,
    Medium,
    Fast
}

public enum SpectrumMediaColorMode
{
    Vivid,
    Soft,
    Tinted
}

public enum SystemMediaCoverStatus
{
    Stopped,
    Starting,
    Available,
    Unavailable,
    Unsupported,
    Faulted
}

public sealed record SpectrumPalette(Color Primary, Color Secondary, bool IsGrayscale = false);
