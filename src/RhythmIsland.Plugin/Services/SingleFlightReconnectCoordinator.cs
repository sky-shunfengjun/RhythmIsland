namespace RhythmIsland.Services;

internal sealed class SingleFlightReconnectCoordinator
{
    internal static readonly TimeSpan[] DefaultRetryDelays =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    private readonly object _sync = new();
    private readonly Func<CancellationToken, Task<bool>> _restart;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly IReadOnlyList<TimeSpan> _retryDelays;
    private readonly CancellationToken _cancellationToken;
    private Task _completion = Task.CompletedTask;

    internal SingleFlightReconnectCoordinator(
        Func<CancellationToken, Task<bool>> restart,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        _restart = restart;
        _cancellationToken = cancellationToken;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
        _retryDelays = retryDelays ?? DefaultRetryDelays;
        if (_retryDelays.Count == 0) throw new ArgumentException("重连延迟不能为空。", nameof(retryDelays));
    }

    internal Task Completion
    {
        get { lock (_sync) return _completion; }
    }

    internal bool Request(TimeSpan? debounce = null)
    {
        lock (_sync)
        {
            if (_cancellationToken.IsCancellationRequested || !_completion.IsCompleted) return false;
            _completion = RunAsync(debounce);
            return true;
        }
    }

    private async Task RunAsync(TimeSpan? debounce)
    {
        var retryIndex = 0;
        var nextDelay = debounce;
        try
        {
            while (!_cancellationToken.IsCancellationRequested)
            {
                var delay = nextDelay ?? _retryDelays[Math.Min(retryIndex++, _retryDelays.Count - 1)];
                nextDelay = null;
                await _delay(delay, _cancellationToken);
                if (await _restart(_cancellationToken)) return;
            }
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
        }
    }
}
