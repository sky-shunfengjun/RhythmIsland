using Avalonia.Media;
using RhythmIsland.Models;
using RhythmIsland.Services;
using SkiaSharp;
using Microsoft.Extensions.Logging.Abstractions;

namespace RhythmIsland.Tests;

public sealed class SystemMediaCoverTests
{
    [Fact]
    public void ColorfulCoverProducesTwoDistinctVisibleColors()
    {
        var pixels = Enumerable.Repeat(new CoverPixel(235, 40, 90), 70)
            .Concat(Enumerable.Repeat(new CoverPixel(40, 120, 245), 30))
            .ToArray();

        var palette = CoverPaletteExtractor.Extract(pixels);

        Assert.NotNull(palette);
        Assert.NotEqual(palette!.Primary, palette.Secondary);
        Assert.False(palette.IsGrayscale);
        Assert.True(palette.Primary.R > palette.Primary.B);
        Assert.True(palette.Secondary.B > palette.Secondary.R);
    }

    [Fact]
    public void GrayscaleCoverProducesDistinctNeutralPaletteAndTransparentCoverFallsBack()
    {
        var palette = CoverPaletteExtractor.Extract(
            Enumerable.Repeat(new CoverPixel(128, 128, 128), 100).ToArray());

        Assert.NotNull(palette);
        Assert.NotEqual(palette!.Primary, palette.Secondary);
        Assert.True(palette.IsGrayscale);
        Assert.Equal(palette.Primary.R, palette.Primary.G);
        Assert.Equal(palette.Primary.G, palette.Primary.B);
        Assert.Equal(palette.Secondary.R, palette.Secondary.G);
        Assert.Equal(palette.Secondary.G, palette.Secondary.B);
        Assert.Null(CoverPaletteExtractor.Extract(Enumerable.Repeat(new CoverPixel(255, 0, 0, 0), 100).ToArray()));
    }

    [Fact]
    public void EncodedCoverLargerThanPreviousDimensionLimitIsAcceptedWhenPixelCountIsSafe()
    {
        using var bitmap = new SKBitmap(2_200, 32);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.SlateBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        var palette = SystemMediaCoverService.ExtractPalette(data.ToArray());

        Assert.NotNull(palette);
    }

    [Fact]
    public void DarkCoverColorsAreRaisedToVisibleRange()
    {
        var pixels = Enumerable.Repeat(new CoverPixel(30, 3, 40), 60)
            .Concat(Enumerable.Repeat(new CoverPixel(3, 35, 30), 40))
            .ToArray();

        var palette = CoverPaletteExtractor.Extract(pixels);

        Assert.NotNull(palette);
        Assert.True(Math.Max(palette!.Primary.R, Math.Max(palette.Primary.G, palette.Primary.B)) >= 100);
    }

    [Fact]
    public void EncodedCoverIsDownscaledAndExtractedWithoutSavingImage()
    {
        using var bitmap = new SKBitmap(512, 512);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Magenta);
            canvas.DrawRect(256, 0, 256, 512, new SKPaint { Color = SKColors.Cyan });
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        var palette = SystemMediaCoverService.ExtractPalette(data.ToArray());

        Assert.NotNull(palette);
        Assert.NotEqual(palette!.Primary, palette.Secondary);
    }

    [Fact]
    public void InvalidAndOversizedEncodedCoverFallsBack()
    {
        Assert.Null(SystemMediaCoverService.ExtractPalette([1, 2, 3, 4]));
        Assert.Null(SystemMediaCoverService.ExtractPalette(new byte[10 * 1024 * 1024 + 1]));
    }

    [Fact]
    public async Task UnsupportedSystemStartsOnceAndStopsAfterLastLease()
    {
        using var service = new SystemMediaCoverService(
            NullLogger<SystemMediaCoverService>.Instance,
            () => false);

        using var first = service.Acquire();
        using var second = service.Acquire();
        await service.LifecycleTask;

        Assert.Equal(2, service.ConsumerCount);
        Assert.Equal(SystemMediaCoverStatus.Unsupported, service.Status);

        second.Dispose();
        Assert.Equal(1, service.ConsumerCount);
        first.Dispose();
        await service.LifecycleTask;

        Assert.Equal(0, service.ConsumerCount);
        Assert.Equal(SystemMediaCoverStatus.Stopped, service.Status);
        Assert.Null(service.CurrentPalette);
    }
}
