using Windows.Foundation.Metadata;
using Windows.Media.Control;
using Microsoft.Extensions.Logging;
using RhythmIsland.Abstractions;
using RhythmIsland.Models;
using SkiaSharp;

namespace RhythmIsland.Services;

public sealed class SystemMediaCoverService : ISystemMediaCoverService, IDisposable
{
    private const int MaximumEncodedBytes = 10 * 1024 * 1024;
    private const long MaximumSourcePixels = 4_096L * 4_096L;
    private const int PaletteDimension = 64;
    private const string SessionManagerTypeName =
        "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager";

    private readonly object _sync = new();
    private readonly ILogger<SystemMediaCoverService> _logger;
    private readonly Func<bool> _apiAvailabilityProbe;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private CancellationTokenSource? _refreshCancellation;
    private Task _lifecycleTask = Task.CompletedTask;
    private int _consumerCount;
    private bool _desiredRunning;
    private bool _disposed;
    private bool _initializationFailureLogged;
    private SpectrumPalette? _currentPalette;
    private SystemMediaCoverStatus _status = SystemMediaCoverStatus.Stopped;
    private string _statusText = "未启用音乐封面取色。";

    public SystemMediaCoverService(ILogger<SystemMediaCoverService> logger) : this(logger, IsApiAvailable)
    {
    }

    internal SystemMediaCoverService(ILogger<SystemMediaCoverService> logger, Func<bool> apiAvailabilityProbe)
    {
        _logger = logger;
        _apiAvailabilityProbe = apiAvailabilityProbe;
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

    public IDisposable Acquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Interlocked.Increment(ref _consumerCount) == 1) QueueLifecycle(true);
        return new Lease(this);
    }

    private void Release()
    {
        var remaining = Interlocked.Decrement(ref _consumerCount);
        if (remaining <= 0)
        {
            Interlocked.Exchange(ref _consumerCount, 0);
            QueueLifecycle(false);
        }
    }

    private void QueueLifecycle(bool shouldRun)
    {
        lock (_sync)
        {
            if (_disposed) shouldRun = false;
            _desiredRunning = shouldRun;
            _lifecycleTask = _lifecycleTask.ContinueWith(
                _ => ReconcileAsync(),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
        }
    }

    private async Task ReconcileAsync()
    {
        bool shouldRun;
        lock (_sync) shouldRun = _desiredRunning && !_disposed;
        if (!shouldRun)
        {
            StopCore();
            return;
        }

        if (_manager is not null)
        {
            ScheduleRefresh();
            return;
        }

        SetState(SystemMediaCoverStatus.Starting, "正在获取 Windows 媒体封面…", null);
        try
        {
            if (!_apiAvailabilityProbe())
            {
                SetState(SystemMediaCoverStatus.Unsupported, "当前 Windows 版本无法获取媒体封面，正在使用主题色。", null);
                return;
            }

            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            lock (_sync)
            {
                if (!_desiredRunning || _disposed) return;
                _manager = manager;
                _manager.CurrentSessionChanged += OnCurrentSessionChanged;
                _manager.SessionsChanged += OnSessionsChanged;
                _initializationFailureLogged = false;
            }
            ScheduleRefresh();
        }
        catch (Exception exception)
        {
            if (!_initializationFailureLogged)
            {
                _initializationFailureLogged = true;
                _logger.LogWarning(exception, "无法初始化 Windows 媒体封面服务，将使用主题色。");
            }
            SetState(SystemMediaCoverStatus.Faulted, "当前无法获取媒体封面，正在使用主题色。", null);
        }
    }

    internal static bool IsApiAvailable()
    {
        try { return ApiInformation.IsTypePresent(SessionManagerTypeName); }
        catch { return false; }
    }

    private void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs eventArgs) => ScheduleRefresh();

    private void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs eventArgs) => ScheduleRefresh();

    private void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs eventArgs) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        CancellationToken token;
        lock (_sync)
        {
            if (!_desiredRunning || _disposed || _manager is null) return;
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation = new CancellationTokenSource();
            token = _refreshCancellation.Token;
        }
        _ = RefreshAsync(token);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            GlobalSystemMediaTransportControlsSessionManager? manager;
            lock (_sync) manager = _manager;
            if (manager is null) return;

            var session = manager.GetCurrentSession();
            AttachSession(session);
            if (session is null)
            {
                SetState(SystemMediaCoverStatus.Unavailable, "当前没有可用的媒体封面，正在使用主题色。", null);
                return;
            }

            var properties = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (properties?.Thumbnail is null)
            {
                SetState(SystemMediaCoverStatus.Unavailable, "当前媒体没有封面，正在使用主题色。", null);
                return;
            }

            using var randomAccessStream = await properties.Thumbnail.OpenReadAsync().AsTask(cancellationToken);
            if (randomAccessStream.Size == 0 || randomAccessStream.Size > MaximumEncodedBytes)
            {
                SetState(SystemMediaCoverStatus.Unavailable, "当前媒体封面无法用于取色，正在使用主题色。", null);
                return;
            }

            using var stream = randomAccessStream.AsStreamForRead();
            using var memory = new MemoryStream((int)randomAccessStream.Size);
            await stream.CopyToAsync(memory, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (memory.Length > MaximumEncodedBytes)
            {
                SetState(SystemMediaCoverStatus.Unavailable, "当前媒体封面过大，正在使用主题色。", null);
                return;
            }

            var palette = await Task.Run(() => ExtractPalette(memory.ToArray()), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            SetState(
                palette is null ? SystemMediaCoverStatus.Unavailable : SystemMediaCoverStatus.Available,
                palette is null ? "当前媒体封面无法用于取色，正在使用主题色。" : "已识别当前媒体封面，正在使用封面配色。",
                palette);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "读取 Windows 媒体封面失败。");
            SetState(SystemMediaCoverStatus.Unavailable, "当前无法读取媒体封面，正在使用主题色。", null);
        }
    }

    internal static SpectrumPalette? ExtractPalette(byte[] encodedImage)
    {
        if (encodedImage.Length == 0 || encodedImage.Length > MaximumEncodedBytes) return null;
        using var data = SKData.CreateCopy(encodedImage);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
            (long)codec.Info.Width * codec.Info.Height > MaximumSourcePixels)
            return null;

        using var decoded = SKBitmap.Decode(data);
        if (decoded is null) return null;
        var width = Math.Min(PaletteDimension, decoded.Width);
        var height = Math.Min(PaletteDimension, decoded.Height);

        var pixels = new CoverPixel[width * height];
        var index = 0;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var sourceX = Math.Min(decoded.Width - 1, (int)((x + 0.5) * decoded.Width / width));
            var sourceY = Math.Min(decoded.Height - 1, (int)((y + 0.5) * decoded.Height / height));
            var color = decoded.GetPixel(sourceX, sourceY);
            pixels[index++] = new CoverPixel(color.Red, color.Green, color.Blue, color.Alpha);
        }
        return CoverPaletteExtractor.Extract(pixels);
    }

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        lock (_sync)
        {
            if (!_desiredRunning || _disposed || _manager is null) return;
            if (ReferenceEquals(_session, session)) return;
            if (_session is not null) _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session = session;
            if (_session is not null) _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        }
    }

    private void StopCore()
    {
        lock (_sync)
        {
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation = null;
            if (_session is not null) _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session = null;
            if (_manager is not null)
            {
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _manager.SessionsChanged -= OnSessionsChanged;
            }
            _manager = null;
        }
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
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _desiredRunning = false;
        }
        StopCore();
    }

    private sealed class Lease(SystemMediaCoverService owner) : IDisposable
    {
        private SystemMediaCoverService? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
