using Avalonia.Media;
using RhythmIsland.Models;

namespace RhythmIsland.Controls.Components;

internal static class SpectrumColorHelper
{
    private const double HueOffsetDegrees = 45;
    private const double GrayscaleSaturationThreshold = 0.15;

    internal static Color CreateAutomaticGradientEnd(Color start)
    {
        var red = start.R / 255d;
        var green = start.G / 255d;
        var blue = start.B / 255d;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;
        var saturation = max <= 0 ? 0 : delta / max;

        if (saturation < GrayscaleSaturationThreshold)
            return Color.FromArgb(start.A, 0x4D, 0xA3, 0xFF);

        double hue;
        if (max == red)
            hue = 60 * (((green - blue) / delta) % 6);
        else if (max == green)
            hue = 60 * ((blue - red) / delta + 2);
        else
            hue = 60 * ((red - green) / delta + 4);

        if (hue < 0) hue += 360;
        hue = (hue + HueOffsetDegrees) % 360;
        return FromHsv(start.A, hue, saturation, max);
    }

    internal static Color Interpolate(Color start, Color end, double amount)
    {
        var value = double.IsFinite(amount) ? Math.Clamp(amount, 0, 1) : 0;
        static byte Blend(byte from, byte to, double factor) => (byte)Math.Clamp(
            (int)Math.Round(from + (to - from) * factor, MidpointRounding.AwayFromZero), 0, 255);

        return Color.FromArgb(
            Blend(start.A, end.A, value),
            Blend(start.R, end.R, value),
            Blend(start.G, end.G, value),
            Blend(start.B, end.B, value));
    }

    internal static Color Lighten(Color color, double amount) =>
        Interpolate(color, Color.FromArgb(color.A, 255, 255, 255), amount);

    internal static double DynamicGradientPeriodSeconds(SpectrumGradientSpeed speed) => speed switch
    {
        SpectrumGradientSpeed.Slow => 10,
        SpectrumGradientSpeed.Fast => 3,
        _ => 6
    };

    internal static double DynamicGradientPhase(SpectrumGradientSpeed speed, TimeSpan elapsed)
    {
        var period = DynamicGradientPeriodSeconds(speed);
        var seconds = double.IsFinite(elapsed.TotalSeconds) ? Math.Max(0, elapsed.TotalSeconds) : 0;
        return seconds % period / period;
    }

    internal static Color SampleDynamicGradient(Color primary, Color secondary, double position, double phase)
    {
        var wrapped = ((position + phase) % 1 + 1) % 1;
        var amount = wrapped <= 0.5 ? wrapped * 2 : (1 - wrapped) * 2;
        return Interpolate(primary, secondary, amount);
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
            (int)Math.Round((channel) * 255, MidpointRounding.AwayFromZero), 0, 255);

        return Color.FromArgb(alpha, ToByte(red + match), ToByte(green + match), ToByte(blue + match));
    }
}
