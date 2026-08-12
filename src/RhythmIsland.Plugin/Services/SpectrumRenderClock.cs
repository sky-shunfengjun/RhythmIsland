using Avalonia.Threading;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using RhythmIsland.Abstractions;

namespace RhythmIsland.Services;

public sealed class SpectrumRenderClock : ISpectrumRenderClock, IDisposable
{
    private readonly HashSet<Action> _subscribers = [];
    private readonly DispatcherTimer _timer;
    private readonly ILogger<SpectrumRenderClock> _logger;
    private readonly RhythmIslandSettingsStore _settingsStore;
    private bool _disposed;

    public SpectrumRenderClock(RhythmIslandSettingsStore settingsStore, ILogger<SpectrumRenderClock> logger)
    {
        _settingsStore = settingsStore;
        _logger = logger;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = IntervalFor(settingsStore.Settings.FrameRate) };
        _timer.Tick += OnTick;
        settingsStore.Settings.PropertyChanged += OnSettingsChanged;
    }

    internal int SubscriberCount => _subscribers.Count;
    internal bool IsRunning => _timer.IsEnabled;

    public IDisposable Subscribe(Action callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callback);
        _subscribers.Add(callback);
        if (_subscribers.Count == 1) _timer.Start();
        return new Subscription(this, callback);
    }

    private void Unsubscribe(Action callback)
    {
        _subscribers.Remove(callback);
        if (_subscribers.Count == 0) _timer.Stop();
    }

    private void OnTick(object? sender, EventArgs eventArgs)
    {
        foreach (var callback in _subscribers.ToArray())
        {
            try { callback(); }
            catch (Exception exception) { _logger.LogError(exception, "刷新律动岛频谱组件失败。"); }
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(_settingsStore.Settings.FrameRate))
            _timer.Interval = IntervalFor(_settingsStore.Settings.FrameRate);
    }

    private static TimeSpan IntervalFor(int frameRate) => TimeSpan.FromSeconds(1d / Math.Clamp(frameRate, 1, 60));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _settingsStore.Settings.PropertyChanged -= OnSettingsChanged;
        _subscribers.Clear();
    }

    private sealed class Subscription(SpectrumRenderClock owner, Action callback) : IDisposable
    {
        private SpectrumRenderClock? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Unsubscribe(callback);
    }
}
