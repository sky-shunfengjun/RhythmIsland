using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Windows.Foundation.Metadata;
using Microsoft.Extensions.Logging;
using RhythmIsland.Abstractions;
using RhythmIsland.Models;
using SkiaSharp;

namespace RhythmIsland.Services;

public sealed class SystemMediaCoverService : ISystemMediaCoverService, IDisposable
{
    private const int MaximumEncodedBytes = 10 * 1024 * 1024;
    private const long MaximumSourcePixels = 4_096L * 4_096L;
    private const long MaximumIntermediatePixels = 2_048L * 2_048L;
    private const int PaletteDimension = 64;
    private const string SessionManagerTypeName =
        "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager";

    private readonly object _sync = new();
    private readonly ILogger<SystemMediaCoverService> _logger;
    private readonly Func<bool> _apiAvailabilityProbe;
    private readonly Func<CancellationToken, Task<ISystemMediaSessionBackend>> _backendFactory;
    private ISystemMediaSessionBackend? _backend;
    private CancellationTokenSource? _initializationCancellation;
    private CancellationTokenSource? _runCancellation;
    private CancellationTokenSource? _refreshCancellation;
    private Channel<long>? _refreshRequests;
    private Task _refreshTask = Task.CompletedTask;
    private Task _lifecycleTask = Task.CompletedTask;
    private int _consumerCount;
    private long _refreshVersion;
    private bool _desiredRunning;
    private bool _disposed;
    private bool _initializationFailureLogged;
    private SpectrumPalette? _currentPalette;
    private SystemMediaCoverStatus _status = SystemMediaCoverStatus.Stopped;
    private string _statusText = "未启用音乐封面取色。";

    public SystemMediaCoverService(ILogger<SystemMediaCoverService> logger) : this(
        logger, IsApiAvailable, CreateBackendAfterAvailabilityCheckAsync)
    {
    }

    internal SystemMediaCoverService(
        ILogger<SystemMediaCoverService> logger,
        Func<bool> apiAvailabilityProbe,
        Func<CancellationToken, Task<ISystemMediaSessionBackend>>? backendFactory = null)
    {
        _logger = logger;
        _apiAvailabilityProbe = apiAvailabilityProbe;
        _backendFactory = backendFactory ?? CreateBackendAfterAvailabilityCheckAsync;
    }

    public event EventHandler? Changed;

    public SpectrumPalette? CurrentPalette
    {
        get { lock (_sync) return _currentPalette; }
    }

    public SystemMediaCoverStatus Status
    {
        get { lock (_sync) return _status; }
    }

    public string StatusText
    {
        get { lock (_sync) return _statusText; }
    }

    internal int ConsumerCount => Volatile.Read(ref _consumerCount);
    internal Task LifecycleTask
    {
        get { lock (_sync) return _lifecycleTask; }
    }
    internal Task RefreshTask
    {
        get { lock (_sync) return _refreshTask; }
    }

    public IDisposable Acquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Interlocked.Increment(ref _consumerCount) == 1) QueueLifecycle(true);
        return new Lease(this);
    }

    private void Release()
    {
        var remaining = Interlocked.Decrement(ref _consumerCount);
        if (remaining > 0) return;
        Interlocked.Exchange(ref _consumerCount, 0);
        QueueLifecycle(false);
    }

    private void QueueLifecycle(bool shouldRun)
    {
        CancellationTokenSource? initializationCancellation = null;
        lock (_sync)
        {
            if (_disposed) return;
            _desiredRunning = shouldRun;
            if (!shouldRun) initializationCancellation = _initializationCancellation;
            _lifecycleTask = _lifecycleTask.ContinueWith(
                _ => ReconcileAsync(), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
        initializationCancellation?.Cancel();
    }

    private async Task ReconcileAsync()
    {
        CancellationTokenSource? initializationCancellation;
        lock (_sync)
        {
            if (!_desiredRunning || _disposed)
            {
                initializationCancellation = null;
            }
            else if (_backend is not null)
            {
                RequestRefresh();
                return;
            }
            else
            {
                initializationCancellation = new CancellationTokenSource();
                _initializationCancellation = initializationCancellation;
            }
        }

        if (initializationCancellation is null)
        {
            await StopCoreAsync();
            return;
        }

        SetState(SystemMediaCoverStatus.Starting, "正在获取 Windows 媒体封面…", null);
        try
        {
            if (!_apiAvailabilityProbe())
            {
                SetState(SystemMediaCoverStatus.Unsupported,
                    "当前 Windows 版本无法获取媒体封面，正在使用主题色。", null);
                return;
            }

            var backend = await _backendFactory(initializationCancellation.Token);
            var publishBackend = false;
            lock (_sync)
            {
                if (!initializationCancellation.IsCancellationRequested &&
                    _desiredRunning && !_disposed &&
                    ReferenceEquals(_initializationCancellation, initializationCancellation))
                {
                    _backend = backend;
                    _backend.Changed += OnBackendChanged;
                    _runCancellation = new CancellationTokenSource();
                    _refreshRequests = Channel.CreateBounded<long>(new BoundedChannelOptions(1)
                    {
                        FullMode = BoundedChannelFullMode.DropOldest,
                        SingleReader = true,
                        SingleWriter = false
                    });
                    _refreshTask = RunRefreshLoopAsync(
                        _refreshRequests.Reader, backend, _runCancellation.Token);
                    _initializationFailureLogged = false;
                    publishBackend = true;
                }
            }
            if (!publishBackend)
            {
                backend.Dispose();
                return;
            }
            RequestRefresh();
        }
        catch (OperationCanceledException) when (initializationCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            var shouldReport = false;
            lock (_sync)
            {
                if (_desiredRunning && !_disposed &&
                    ReferenceEquals(_initializationCancellation, initializationCancellation))
                {
                    shouldReport = true;
                    if (!_initializationFailureLogged)
                    {
                        _initializationFailureLogged = true;
                        _logger.LogWarning(exception, "无法初始化 Windows 媒体封面服务，将使用主题色。");
                    }
                }
            }
            if (shouldReport)
                SetState(SystemMediaCoverStatus.Faulted,
                    "当前无法获取媒体封面，正在使用主题色。", null);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_initializationCancellation, initializationCancellation))
                    _initializationCancellation = null;
            }
            initializationCancellation.Dispose();
        }
    }

    internal static bool IsApiAvailable()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return false;
        try { return ApiInformation.IsTypePresent(SessionManagerTypeName); }
        catch { return false; }
    }

    [UnconditionalSuppressMessage("Interoperability", "CA1416",
        Justification = "Only called after the Windows 10 1809 and ApiInformation runtime guard succeeds.")]
    private static Task<ISystemMediaSessionBackend> CreateBackendAfterAvailabilityCheckAsync(
        CancellationToken cancellationToken) => WindowsSystemMediaSessionBackend.CreateAsync(cancellationToken);

    private void OnBackendChanged(object? sender, EventArgs eventArgs) => RequestRefresh();

    private void RequestRefresh()
    {
        Channel<long>? requests;
        long version;
        lock (_sync)
        {
            if (!_desiredRunning || _disposed || _backend is null || _refreshRequests is null) return;
            version = ++_refreshVersion;
            _refreshCancellation?.Cancel();
            requests = _refreshRequests;
        }
        requests.Writer.TryWrite(version);
    }

    private async Task RunRefreshLoopAsync(
        ChannelReader<long> reader,
        ISystemMediaSessionBackend backend,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.WaitToReadAsync(cancellationToken))
            {
                if (!reader.TryRead(out var version)) continue;
                while (reader.TryRead(out var newerVersion)) version = newerVersion;

                using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                lock (_sync) _refreshCancellation = operation;
                await RefreshOnceAsync(backend, version, operation.Token);
                lock (_sync)
                {
                    if (ReferenceEquals(_refreshCancellation, operation)) _refreshCancellation = null;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshOnceAsync(
        ISystemMediaSessionBackend backend,
        long version,
        CancellationToken cancellationToken)
    {
        try
        {
            var thumbnail = await backend.ReadCurrentThumbnailAsync(MaximumEncodedBytes, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (thumbnail is null)
            {
                TrySetRefreshState(version, SystemMediaCoverStatus.Unavailable,
                    "当前媒体没有可用封面，正在使用主题色。", null);
                return;
            }

            var palette = await Task.Run(() => ExtractPalette(thumbnail), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            TrySetRefreshState(
                version,
                palette is null ? SystemMediaCoverStatus.Unavailable : SystemMediaCoverStatus.Available,
                palette is null
                    ? "当前媒体封面无法用于取色，正在使用主题色。"
                    : "已识别当前媒体封面，正在使用封面配色。",
                palette);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "读取 Windows 媒体封面失败。");
            TrySetRefreshState(version, SystemMediaCoverStatus.Unavailable,
                "当前无法读取媒体封面，正在使用主题色。", null);
        }
    }

    private void TrySetRefreshState(
        long version,
        SystemMediaCoverStatus status,
        string text,
        SpectrumPalette? palette)
    {
        var changed = false;
        lock (_sync)
        {
            if (!_desiredRunning || _disposed || version != _refreshVersion) return;
            if (_status != status || _statusText != text || _currentPalette != palette)
            {
                _status = status;
                _statusText = text;
                _currentPalette = palette;
                changed = true;
            }
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    internal static SpectrumPalette? ExtractPalette(byte[] encodedImage)
    {
        var decoded = DecodeThumbnail(encodedImage);
        return decoded is null ? null : CoverPaletteExtractor.Extract(decoded.Value.Pixels);
    }

    internal static DecodedCover? DecodeThumbnail(byte[] encodedImage)
    {
        if (encodedImage.Length == 0 || encodedImage.Length > MaximumEncodedBytes) return null;
        using var data = SKData.CreateCopy(encodedImage);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
            (long)codec.Info.Width * codec.Info.Height > MaximumSourcePixels)
            return null;

        var targetScale = Math.Min(1f, PaletteDimension / (float)Math.Max(codec.Info.Width, codec.Info.Height));
        var codecDimensions = codec.GetScaledDimensions(targetScale);
        if ((long)codecDimensions.Width * codecDimensions.Height > MaximumIntermediatePixels) return null;

        var decodeInfo = new SKImageInfo(
            Math.Max(1, codecDimensions.Width), Math.Max(1, codecDimensions.Height),
            SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var decoded = SKBitmap.Decode(codec, decodeInfo);
        if (decoded is null) return null;

        var outputScale = Math.Min(1d, PaletteDimension / (double)Math.Max(decoded.Width, decoded.Height));
        var width = Math.Clamp((int)Math.Round(decoded.Width * outputScale), 1, PaletteDimension);
        var height = Math.Clamp((int)Math.Round(decoded.Height * outputScale), 1, PaletteDimension);
        using var bitmap = decoded.Width == width && decoded.Height == height
            ? decoded.Copy()
            : decoded.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul),
                SKFilterQuality.Medium);
        if (bitmap is null) return null;

        var pixels = new CoverPixel[width * height];
        var index = 0;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var color = bitmap.GetPixel(x, y);
            pixels[index++] = new CoverPixel(color.Red, color.Green, color.Blue, color.Alpha);
        }
        return new DecodedCover(pixels, width, height);
    }

    private async Task StopCoreAsync()
    {
        CancellationTokenSource? runCancellation;
        CancellationTokenSource? refreshCancellation;
        Channel<long>? requests;
        Task refreshTask;
        ISystemMediaSessionBackend? backend;
        lock (_sync)
        {
            runCancellation = _runCancellation;
            refreshCancellation = _refreshCancellation;
            requests = _refreshRequests;
            refreshTask = _refreshTask;
            backend = _backend;
            _runCancellation = null;
            _refreshCancellation = null;
            _refreshRequests = null;
            _refreshTask = Task.CompletedTask;
            _backend = null;
            ++_refreshVersion;
            if (backend is not null) backend.Changed -= OnBackendChanged;
        }

        refreshCancellation?.Cancel();
        runCancellation?.Cancel();
        requests?.Writer.TryComplete();
        try { await refreshTask; }
        catch (OperationCanceledException) { }
        backend?.Dispose();
        refreshCancellation?.Dispose();
        runCancellation?.Dispose();
        SetState(SystemMediaCoverStatus.Stopped, "未启用音乐封面取色。", null);
    }

    private void SetState(SystemMediaCoverStatus status, string text, SpectrumPalette? palette)
    {
        var changed = false;
        lock (_sync)
        {
            if (_status != status || _statusText != text || _currentPalette != palette)
            {
                _status = status;
                _statusText = text;
                _currentPalette = palette;
                changed = true;
            }
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        CancellationTokenSource? initializationCancellation;
        CancellationTokenSource? refreshCancellation;
        CancellationTokenSource? runCancellation;
        Task lifecycleTask;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _desiredRunning = false;
            initializationCancellation = _initializationCancellation;
            refreshCancellation = _refreshCancellation;
            runCancellation = _runCancellation;
            lifecycleTask = _lifecycleTask;
        }
        initializationCancellation?.Cancel();
        refreshCancellation?.Cancel();
        runCancellation?.Cancel();
        lifecycleTask.GetAwaiter().GetResult();
        StopCoreAsync().GetAwaiter().GetResult();
    }

    internal readonly record struct DecodedCover(CoverPixel[] Pixels, int Width, int Height);

    private sealed class Lease(SystemMediaCoverService owner) : IDisposable
    {
        private SystemMediaCoverService? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
