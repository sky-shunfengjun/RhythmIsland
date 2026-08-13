using Avalonia.Media;
using RhythmIsland.Models;

namespace RhythmIsland.Controls.Components;

internal static class SpectrumMediaPaletteProcessor
{
    private static readonly Color DefaultAccentColor = Color.Parse("#FF4DA3FF");
    private const double GrayscaleSaturationThreshold = 0.15;

    internal static SpectrumPalette Process(
        SpectrumPalette source,
        SpectrumMediaColorMode mode,
        Color themeColor)
    {
        var validatedMode = Enum.IsDefined(mode) ? mode : SpectrumMediaColorMode.Vivid;
        if (source.IsGrayscale)
            return ProcessGrayscale(source, validatedMode, ResolveChromaticThemeColor(themeColor));

        return validatedMode switch
        {
            SpectrumMediaColorMode.Soft => new SpectrumPalette(
                Adjust(source.Primary, 0.30, 0.55, 0.62, 0.88),
                Adjust(source.Secondary, 0.30, 0.55, 0.62, 0.88)),
            SpectrumMediaColorMode.Tinted => CreateTinted(source.Primary),
            _ => new SpectrumPalette(
                Adjust(source.Primary, 0.68, 0.95, 0.68, 0.95),
                Adjust(source.Secondary, 0.68, 0.95, 0.68, 0.95))
        };
    }

    private static SpectrumPalette ProcessGrayscale(
        SpectrumPalette source,
        SpectrumMediaColorMode mode,
        Color themeColor) => mode switch
    {
        SpectrumMediaColorMode.Soft => new SpectrumPalette(
            CreateNeutral(source.Primary, 0.62, 0.88),
            CreateNeutral(source.Secondary, 0.62, 0.88),
            true),
        SpectrumMediaColorMode.Tinted => CreateTinted(themeColor),
        _ => new SpectrumPalette(
            Adjust(themeColor, 0.68, 0.95, 0.68, 0.95),
            Adjust(SpectrumColorHelper.CreateAutomaticGradientEnd(themeColor), 0.68, 0.95, 0.68, 0.95))
    };

    private static SpectrumPalette CreateTinted(Color color)
    {
        var hsv = ToHsv(color);
        return new SpectrumPalette(
            FromHsv(color.A, hsv.Hue, 0.70, 0.85),
            FromHsv(color.A, hsv.Hue, 0.70, 0.58));
    }

    private static Color ResolveChromaticThemeColor(Color themeColor) =>
        ToHsv(themeColor).Saturation < GrayscaleSaturationThreshold ? DefaultAccentColor : themeColor;

    private static Color Adjust(
        Color color,
        double minimumSaturation,
        double maximumSaturation,
        double minimumValue,
        double maximumValue)
    {
        var hsv = ToHsv(color);
        return FromHsv(
            color.A,
            hsv.Hue,
            Math.Clamp(hsv.Saturation, minimumSaturation, maximumSaturation),
            Math.Clamp(hsv.Value, minimumValue, maximumValue));
    }

    private static Color CreateNeutral(Color color, double minimumValue, double maximumValue)
    {
        var hsv = ToHsv(color);
        var value = Math.Clamp(hsv.Value, minimumValue, maximumValue);
        var channel = (byte)Math.Clamp(
            (int)Math.Round(value * 255, MidpointRounding.AwayFromZero), 0, 255);
        return Color.FromArgb(color.A, channel, channel, channel);
    }

    internal static (double Hue, double Saturation, double Value) ToHsv(Color color)
    {
        var red = color.R / 255d;
        var green = color.G / 255d;
        var blue = color.B / 255d;
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        var delta = maximum - minimum;
        var saturation = maximum <= 0 ? 0 : delta / maximum;
        if (delta <= double.Epsilon) return (0, saturation, maximum);

        var hue = maximum == red
            ? 60 * (((green - blue) / delta) % 6)
            : maximum == green
                ? 60 * ((blue - red) / delta + 2)
                : 60 * ((red - green) / delta + 4);
        if (hue < 0) hue += 360;
        return (hue, saturation, maximum);
    }

    private static Color FromHsv(byte alpha, double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var segment = hue / 60;
        var secondary = chroma * (1 - Math.Abs(segment % 2 - 1));
        var (red, green, blue) = segment switch
        {
            < 1 => (chroma, secondary, 0d),
            < 2 => (secondary, chroma, 0d),
            < 3 => (0d, chroma, secondary),
            < 4 => (0d, secondary, chroma),
            < 5 => (secondary, 0d, chroma),
            _ => (chroma, 0d, secondary)
        };
        var match = value - chroma;

        static byte ToByte(double channel) => (byte)Math.Clamp(
            (int)Math.Round(channel * 255, MidpointRounding.AwayFromZero), 0, 255);
        return Color.FromArgb(alpha, ToByte(red + match), ToByte(green + match), ToByte(blue + match));
    }
}
