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
    public void LargeCoverIsDecodedDirectlyWithinPaletteDimensions()
    {
        using var bitmap = new SKBitmap(2_048, 1_024);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);

        var decoded = SystemMediaCoverService.DecodeThumbnail(data.ToArray());

        Assert.NotNull(decoded);
        Assert.InRange(decoded.Value.Width, 1, 64);
        Assert.InRange(decoded.Value.Height, 1, 64);
        Assert.True(decoded.Value.Pixels.Length <= 64 * 64);
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

    [Fact]
    public async Task RapidMediaChangesPublishOnlyNewestCoverAndUseOneRefreshLoop()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstImage = CreateSolidImage(SKColors.Red);
        var secondImage = CreateSolidImage(SKColors.Blue);
        var backend = new FakeMediaBackend(async (request, cancellationToken) =>
        {
            if (request == 1)
            {
                firstStarted.SetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
                return firstImage;
            }
            return secondImage;
        });
        using var service = new SystemMediaCoverService(
            NullLogger<SystemMediaCoverService>.Instance,
            () => true,
            _ => Task.FromResult<ISystemMediaSessionBackend>(backend));

        using var lease = service.Acquire();
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        backend.SignalChanged();
        releaseFirst.SetResult();
        await WaitUntilAsync(() => service.Status == SystemMediaCoverStatus.Available);

        Assert.True(backend.MaximumConcurrentReads <= 1);
        Assert.True(backend.ReadCount >= 2);
        Assert.NotNull(service.CurrentPalette);
        Assert.True(service.CurrentPalette!.Primary.B > service.CurrentPalette.Primary.R);
    }

    [Fact]
    public async Task ReleasingLastLeaseWaitsForRefreshAndPreventsLateStateWrite()
    {
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new FakeMediaBackend(async (_, cancellationToken) =>
        {
            readStarted.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return CreateSolidImage(SKColors.Green);
        });
        using var service = new SystemMediaCoverService(
            NullLogger<SystemMediaCoverService>.Instance,
            () => true,
            _ => Task.FromResult<ISystemMediaSessionBackend>(backend));

        var lease = service.Acquire();
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        lease.Dispose();
        await service.LifecycleTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(SystemMediaCoverStatus.Stopped, service.Status);
        Assert.Null(service.CurrentPalette);
        Assert.True(backend.IsDisposed);
        Assert.Equal(0, backend.ActiveReads);
    }

    private static byte[] CreateSolidImage(SKColor color)
    {
        using var bitmap = new SKBitmap(64, 64);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!predicate())
        {
            if (DateTime.UtcNow >= timeout) throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    private sealed class FakeMediaBackend(
        Func<int, CancellationToken, Task<byte[]?>> read) : ISystemMediaSessionBackend
    {
        private int _activeReads;
        public event EventHandler? Changed;
        public int ReadCount { get; private set; }
        public int ActiveReads => Volatile.Read(ref _activeReads);
        public int MaximumConcurrentReads { get; private set; }
        public bool IsDisposed { get; private set; }

        public async Task<byte[]?> ReadCurrentThumbnailAsync(int maximumBytes, CancellationToken cancellationToken)
        {
            var request = ++ReadCount;
            var active = Interlocked.Increment(ref _activeReads);
            MaximumConcurrentReads = Math.Max(MaximumConcurrentReads, active);
            try { return await read(request, cancellationToken); }
            finally { Interlocked.Decrement(ref _activeReads); }
        }

        public void SignalChanged() => Changed?.Invoke(this, EventArgs.Empty);
        public void Dispose() => IsDisposed = true;
    }
}
