using System.Runtime.Versioning;
using Windows.Media.Control;

namespace RhythmIsland.Services;

[SupportedOSPlatform("windows10.0.17763.0")]
internal sealed class WindowsSystemMediaSessionBackend : ISystemMediaSessionBackend
{
    private readonly object _sync = new();
    private readonly GlobalSystemMediaTransportControlsSessionManager _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _disposed;

    private WindowsSystemMediaSessionBackend(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        _manager = manager;
        _manager.CurrentSessionChanged += OnManagerChanged;
        _manager.SessionsChanged += OnSessionsChanged;
        AttachSession(_manager.GetCurrentSession());
    }

    public event EventHandler? Changed;

    internal static async Task<ISystemMediaSessionBackend> CreateAsync(CancellationToken cancellationToken)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellationToken);
        return new WindowsSystemMediaSessionBackend(manager);
    }

    public async Task<byte[]?> ReadCurrentThumbnailAsync(int maximumBytes, CancellationToken cancellationToken)
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            session = _manager.GetCurrentSession();
            AttachSessionCore(session);
        }

        if (session is null) return null;
        var properties = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (properties?.Thumbnail is null) return null;

        using var randomAccessStream = await properties.Thumbnail.OpenReadAsync().AsTask(cancellationToken);
        if (randomAccessStream.Size == 0 || randomAccessStream.Size > (ulong)maximumBytes) return null;
        using var stream = randomAccessStream.AsStreamForRead();
        using var memory = new MemoryStream((int)randomAccessStream.Size);
        await stream.CopyToAsync(memory, cancellationToken);
        return memory.Length <= maximumBytes ? memory.ToArray() : null;
    }

    private void OnManagerChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs eventArgs)
    {
        lock (_sync)
        {
            if (_disposed) return;
            AttachSessionCore(_manager.GetCurrentSession());
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs eventArgs) => OnManagerChanged(sender, null!);

    private void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs eventArgs) => Changed?.Invoke(this, EventArgs.Empty);

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        lock (_sync) AttachSessionCore(session);
    }

    private void AttachSessionCore(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(_session, session)) return;
        if (_session is not null) _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _session = session;
        if (_session is not null) _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            if (_session is not null) _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session = null;
            _manager.CurrentSessionChanged -= OnManagerChanged;
            _manager.SessionsChanged -= OnSessionsChanged;
        }
    }
}
