using System.Diagnostics;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using RhythmIsland.Abstractions;

namespace RhythmIsland.Services;

public sealed class SpectrumRenderClock : ISpectrumRenderClock, IDisposable
{
    private const int MaximumFrameRate = 240;
    private static readonly long DispatchTolerance = Math.Max(1, Stopwatch.Frequency / 1000);
    private readonly HashSet<SubscriptionState> _subscribers = [];
    private readonly DispatcherTimer _timer;
    private readonly ILogger<SpectrumRenderClock> _logger;
    private bool _disposed;

    public SpectrumRenderClock(ILogger<SpectrumRenderClock> logger)
    {
        _logger = logger;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(1d / 30) };
        _timer.Tick += OnTick;
    }

    internal int SubscriberCount => _subscribers.Count;
    internal bool IsRunning => _timer.IsEnabled;

    public IDisposable Subscribe(Action callback, Func<int> frameRateProvider)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(frameRateProvider);
        var subscription = new SubscriptionState(callback, frameRateProvider);
        _subscribers.Add(subscription);
        RefreshTimerInterval();
        if (_subscribers.Count == 1) _timer.Start();
        return new Subscription(this, subscription);
    }

    private void Unsubscribe(SubscriptionState subscription)
    {
        _subscribers.Remove(subscription);
        if (_subscribers.Count == 0) _timer.Stop();
        else RefreshTimerInterval();
    }

    private void OnTick(object? sender, EventArgs eventArgs)
    {
        var now = Stopwatch.GetTimestamp();
        foreach (var subscription in _subscribers.ToArray())
        {
            try
            {
                RefreshSubscriptionRate(subscription, now);
                if (!ShouldDispatch(subscription, now)) continue;
                subscription.Callback();
                MarkDispatched(subscription, now);
            }
            catch (Exception exception) { _logger.LogError(exception, "刷新律动岛频谱组件失败。"); }
        }
        RefreshTimerInterval();
    }

    internal static bool ShouldDispatch(SubscriptionState subscription, long now)
    {
        return subscription.NextDispatchTimestamp is null ||
               now >= subscription.NextDispatchTimestamp.Value - DispatchTolerance;
    }

    internal static void MarkDispatched(SubscriptionState subscription, long now)
    {
        var period = PeriodTimestampTicks(subscription.EffectiveFrameRate);
        var next = subscription.NextDispatchTimestamp ?? now;
        do { next += period; } while (next <= now);
        subscription.NextDispatchTimestamp = next;
    }

    private void RefreshTimerInterval()
    {
        if (_subscribers.Count == 0) return;
        var now = Stopwatch.GetTimestamp();
        foreach (var subscription in _subscribers) RefreshSubscriptionRate(subscription, now);
        var interval = DelayUntilNextDispatch(_subscribers, now);
        if (_timer.Interval != interval) _timer.Interval = interval;
    }

    private static void RefreshSubscriptionRate(SubscriptionState subscription, long now)
    {
        var effective = NormalizeFrameRate(subscription.FrameRateProvider());
        if (subscription.EffectiveFrameRate == effective) return;
        subscription.EffectiveFrameRate = effective;
        subscription.NextDispatchTimestamp = now;
    }

    internal static TimeSpan DelayUntilNextDispatch(IEnumerable<SubscriptionState> subscriptions, long now)
    {
        var next = subscriptions
            .Select(subscription => subscription.NextDispatchTimestamp ?? now)
            .DefaultIfEmpty(now + PeriodTimestampTicks(30))
            .Min();
        var delayTicks = Math.Max(Stopwatch.Frequency / 1000, next - now);
        return TimeSpan.FromSeconds(delayTicks / (double)Stopwatch.Frequency);
    }

    internal static TimeSpan IntervalFor(IReadOnlyList<int> frameRates)
    {
        var maximum = frameRates
            .Select(NormalizeFrameRate)
            .DefaultIfEmpty(30)
            .Max();
        return TimeSpan.FromSeconds(1d / maximum);
    }

    private static int NormalizeFrameRate(int frameRate) => frameRate is >= 1 and <= MaximumFrameRate
        ? frameRate
        : 30;

    private static long PeriodTimestampTicks(int frameRate) =>
        Math.Max(1, (long)Math.Round(Stopwatch.Frequency / (double)NormalizeFrameRate(frameRate)));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _subscribers.Clear();
    }

    internal sealed class SubscriptionState(Action callback, Func<int> frameRateProvider)
    {
        internal Action Callback { get; } = callback;
        internal Func<int> FrameRateProvider { get; } = frameRateProvider;
        internal int EffectiveFrameRate { get; set; } = 30;
        internal long? NextDispatchTimestamp { get; set; }
    }

    private sealed class Subscription(SpectrumRenderClock owner, SubscriptionState subscription) : IDisposable
    {
        private SpectrumRenderClock? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Unsubscribe(subscription);
    }
}
