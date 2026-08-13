using Avalonia.Media;
using RhythmIsland.Controls.Components;
using RhythmIsland.Models;

namespace RhythmIsland.Tests;

public sealed class SpectrumMediaPaletteProcessorTests
{
    private static readonly SpectrumPalette ColorfulSource = new(
        Color.Parse("#FF542E43"),
        Color.Parse("#FF315C70"));

    [Fact]
    public void ThreeModesProduceDistinctPalettes()
    {
        var vivid = SpectrumMediaPaletteProcessor.Process(
            ColorfulSource, SpectrumMediaColorMode.Vivid, Colors.DodgerBlue);
        var soft = SpectrumMediaPaletteProcessor.Process(
            ColorfulSource, SpectrumMediaColorMode.Soft, Colors.DodgerBlue);
        var tinted = SpectrumMediaPaletteProcessor.Process(
            ColorfulSource, SpectrumMediaColorMode.Tinted, Colors.DodgerBlue);

        Assert.NotEqual(vivid, soft);
        Assert.NotEqual(vivid, tinted);
        Assert.NotEqual(soft, tinted);
    }

    [Fact]
    public void VividRaisesLowSaturationAndBrightnessIntoVisibleRange()
    {
        var result = SpectrumMediaPaletteProcessor.Process(
            ColorfulSource, SpectrumMediaColorMode.Vivid, Colors.DodgerBlue);

        foreach (var color in new[] { result.Primary, result.Secondary })
        {
            var hsv = SpectrumMediaPaletteProcessor.ToHsv(color);
            Assert.InRange(hsv.Saturation, 0.67, 0.951);
            Assert.InRange(hsv.Value, 0.67, 0.951);
        }
    }

    [Fact]
    public void SoftReducesSaturationWithoutBecomingDark()
    {
        var saturated = new SpectrumPalette(Colors.Red, Colors.Blue);
        var result = SpectrumMediaPaletteProcessor.Process(
            saturated, SpectrumMediaColorMode.Soft, Colors.DodgerBlue);

        foreach (var color in new[] { result.Primary, result.Secondary })
        {
            var hsv = SpectrumMediaPaletteProcessor.ToHsv(color);
            Assert.InRange(hsv.Saturation, 0.29, 0.551);
            Assert.InRange(hsv.Value, 0.61, 0.881);
        }
    }

    [Fact]
    public void TintedUsesOneHueWithClearlyDifferentBrightness()
    {
        var result = SpectrumMediaPaletteProcessor.Process(
            ColorfulSource, SpectrumMediaColorMode.Tinted, Colors.DodgerBlue);
        var primary = SpectrumMediaPaletteProcessor.ToHsv(result.Primary);
        var secondary = SpectrumMediaPaletteProcessor.ToHsv(result.Secondary);

        Assert.InRange(Math.Abs(primary.Hue - secondary.Hue), 0, 0.5);
        Assert.Equal(0.70, primary.Saturation, 2);
        Assert.Equal(0.70, secondary.Saturation, 2);
        Assert.Equal(0.85, primary.Value, 2);
        Assert.Equal(0.58, secondary.Value, 2);
    }

    [Theory]
    [InlineData(SpectrumMediaColorMode.Vivid)]
    [InlineData(SpectrumMediaColorMode.Tinted)]
    public void GrayscaleUsesThemeHueForChromaticModes(SpectrumMediaColorMode mode)
    {
        var grayscale = new SpectrumPalette(Colors.Gray, Colors.LightGray, true);
        var result = SpectrumMediaPaletteProcessor.Process(grayscale, mode, Colors.Orange);

        Assert.True(SpectrumMediaPaletteProcessor.ToHsv(result.Primary).Saturation >= 0.68);
        Assert.False(result.IsGrayscale);
    }

    [Fact]
    public void GrayscaleSoftModeStaysNeutralAndVisible()
    {
        var grayscale = new SpectrumPalette(Color.Parse("#FF303030"), Color.Parse("#FFF0F0F0"), true);
        var result = SpectrumMediaPaletteProcessor.Process(
            grayscale, SpectrumMediaColorMode.Soft, Colors.Orange);

        Assert.True(result.IsGrayscale);
        foreach (var color in new[] { result.Primary, result.Secondary })
        {
            Assert.Equal(color.R, color.G);
            Assert.Equal(color.G, color.B);
            Assert.InRange(SpectrumMediaPaletteProcessor.ToHsv(color).Value, 0.61, 0.881);
        }
    }

    [Theory]
    [InlineData(SpectrumMediaColorMode.Vivid)]
    [InlineData(SpectrumMediaColorMode.Tinted)]
    public void GrayscaleThemeFallbackUsesDefaultBlueWhenThemeIsNeutral(SpectrumMediaColorMode mode)
    {
        var grayscale = new SpectrumPalette(Colors.Gray, Colors.White, true);
        var result = SpectrumMediaPaletteProcessor.Process(grayscale, mode, Colors.Gray);

        var hsv = SpectrumMediaPaletteProcessor.ToHsv(result.Primary);
        Assert.InRange(hsv.Hue, 205, 215);
        Assert.True(hsv.Saturation >= 0.68);
    }
}
