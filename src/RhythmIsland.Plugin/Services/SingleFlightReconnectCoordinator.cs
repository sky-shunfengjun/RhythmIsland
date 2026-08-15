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
    private bool _running;
    private bool _restartInProgress;
    private bool _pendingAfterRestart;
    private TimeSpan? _pendingDebounce;

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
            if (_cancellationToken.IsCancellationRequested) return false;
            if (_running)
            {
                if (_restartInProgress)
                {
                    _pendingAfterRestart = true;
                    _pendingDebounce = debounce;
                }
                return false;
            }
            _running = true;
            _completion = RunAsync(debounce);
            return true;
        }
    }

    private async Task RunAsync(TimeSpan? debounce)
    {
        await Task.Yield();
        var retryIndex = 0;
        var nextDelay = debounce;
        try
        {
            while (!_cancellationToken.IsCancellationRequested)
            {
                var delay = nextDelay ?? _retryDelays[Math.Min(retryIndex++, _retryDelays.Count - 1)];
                nextDelay = null;
                await _delay(delay, _cancellationToken);
                lock (_sync)
                {
                    _restartInProgress = true;
                    _pendingAfterRestart = false;
                    _pendingDebounce = null;
                }

                var succeeded = await _restart(_cancellationToken);
                lock (_sync)
                {
                    _restartInProgress = false;
                    if (!succeeded)
                    {
                        _pendingAfterRestart = false;
                        _pendingDebounce = null;
                        continue;
                    }

                    if (!_pendingAfterRestart)
                    {
                        _running = false;
                        return;
                    }

                    nextDelay = _pendingDebounce;
                    retryIndex = 0;
                    _pendingAfterRestart = false;
                    _pendingDebounce = null;
                }
            }
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_sync)
            {
                _running = false;
                _restartInProgress = false;
                _pendingAfterRestart = false;
                _pendingDebounce = null;
            }
        }
    }
}
