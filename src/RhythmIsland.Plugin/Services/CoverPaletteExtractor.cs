using Avalonia.Media;
using RhythmIsland.Models;

namespace RhythmIsland.Services;

internal readonly record struct CoverPixel(byte Red, byte Green, byte Blue, byte Alpha = 255);

internal static class CoverPaletteExtractor
{
    private const double MinimumSaturation = 0.16;

    internal static SpectrumPalette? Extract(ReadOnlySpan<CoverPixel> pixels)
    {
        if (pixels.IsEmpty) return null;

        var buckets = new Dictionary<int, Bucket>();
        long grayscaleRed = 0;
        long grayscaleGreen = 0;
        long grayscaleBlue = 0;
        var grayscaleCount = 0;
        foreach (var pixel in pixels)
        {
            if (pixel.Alpha < 32) continue;
            var hsv = ToHsv(pixel.Red, pixel.Green, pixel.Blue);
            if (hsv.Value < 0.08) continue;
            if (hsv.Saturation < MinimumSaturation)
            {
                grayscaleRed += pixel.Red;
                grayscaleGreen += pixel.Green;
                grayscaleBlue += pixel.Blue;
                grayscaleCount++;
                continue;
            }

            var key = ((pixel.Red >> 4) << 8) | ((pixel.Green >> 4) << 4) | (pixel.Blue >> 4);
            buckets.TryGetValue(key, out var bucket);
            buckets[key] = bucket.Add(pixel);
        }

        if (buckets.Count == 0)
            return grayscaleCount == 0
                ? null
                : CreateGrayscalePalette(
                    (byte)(grayscaleRed / grayscaleCount),
                    (byte)(grayscaleGreen / grayscaleCount),
                    (byte)(grayscaleBlue / grayscaleCount));
        var candidates = buckets.Values
            .Select(bucket => bucket.ToCandidate())
            .OrderByDescending(candidate => candidate.Score)
            .ToArray();
        if (candidates.Length == 0) return null;

        var primary = candidates[0];
        var secondary = candidates
            .Skip(1)
            .Select(candidate => (Candidate: candidate, Distance: ColorDistance(primary.Color, candidate.Color)))
            .Where(entry => entry.Distance >= 0.08)
            .OrderByDescending(entry => entry.Candidate.Score * (0.55 + entry.Distance))
            .Select(entry => (Candidate?)entry.Candidate)
            .FirstOrDefault();

        var primaryColor = MakeVisible(primary.Color);
        var secondaryColor = secondary is null
            ? CreateNeighborColor(primaryColor)
            : MakeVisible(secondary.Value.Color);
        return new SpectrumPalette(primaryColor, secondaryColor);
    }

    private static SpectrumPalette CreateGrayscalePalette(byte red, byte green, byte blue)
    {
        var luminance = (red * 0.2126 + green * 0.7152 + blue * 0.0722) / 255d;
        var primaryValue = Math.Clamp(luminance, 0.48, 0.78);
        var secondaryValue = primaryValue >= 0.64
            ? Math.Max(0.36, primaryValue - 0.26)
            : Math.Min(0.86, primaryValue + 0.26);
        return new SpectrumPalette(CreateNeutral(primaryValue), CreateNeutral(secondaryValue), true);
    }

    private static Color CreateNeutral(double value)
    {
        var channel = (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
        return Color.FromRgb(channel, channel, channel);
    }

    private static Color MakeVisible(Color color)
    {
        var hsv = ToHsv(color.R, color.G, color.B);
        var value = Math.Clamp(hsv.Value, 0.42, 0.92);
        var saturation = Math.Clamp(hsv.Saturation, 0.28, 0.92);
        return FromHsv(hsv.Hue, saturation, value);
    }

    private static Color CreateNeighborColor(Color color)
    {
        var hsv = ToHsv(color.R, color.G, color.B);
        return FromHsv((hsv.Hue + 45) % 360, hsv.Saturation, hsv.Value);
    }

    private static double ColorDistance(Color first, Color second)
    {
        var red = (first.R - second.R) / 255d;
        var green = (first.G - second.G) / 255d;
        var blue = (first.B - second.B) / 255d;
        return Math.Sqrt(red * red * 0.30 + green * green * 0.59 + blue * blue * 0.11);
    }

    private static (double Hue, double Saturation, double Value) ToHsv(byte redByte, byte greenByte, byte blueByte)
    {
        var red = redByte / 255d;
        var green = greenByte / 255d;
        var blue = blueByte / 255d;
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

    private static Color FromHsv(double hue, double saturation, double value)
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
        static byte ToByte(double channel) => (byte)Math.Clamp((int)Math.Round(channel * 255), 0, 255);
        return Color.FromRgb(ToByte(red + match), ToByte(green + match), ToByte(blue + match));
    }

    private readonly record struct Candidate(Color Color, int Count, double Score);

    private readonly record struct Bucket(long Red, long Green, long Blue, int Count)
    {
        internal Bucket Add(CoverPixel pixel) => new(Red + pixel.Red, Green + pixel.Green, Blue + pixel.Blue, Count + 1);

        internal Candidate ToCandidate()
        {
            var color = Color.FromRgb((byte)(Red / Count), (byte)(Green / Count), (byte)(Blue / Count));
            var hsv = ToHsv(color.R, color.G, color.B);
            var brightnessPreference = 1 - Math.Abs(hsv.Value - 0.62) * 0.55;
            return new Candidate(color, Count, Count * (0.50 + hsv.Saturation) * brightnessPreference);
        }
    }
}
