namespace RhythmIsland.Services;

/// <summary>
/// Keeps a cancellation source alive while callers cancel it, without running callbacks under a lock.
/// A late cancellation is unnecessary once the operation owner has disposed its lifetime.
/// </summary>
internal sealed class CancellationLifetime : IDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _source;
    private int _cancelUsers;
    private bool _disposeRequested;

    internal CancellationLifetime() : this(new CancellationTokenSource()) { }

    private CancellationLifetime(CancellationTokenSource source)
    {
        _source = source;
        Token = source.Token;
    }

    internal CancellationToken Token { get; }
    internal bool IsCancellationRequested => Token.IsCancellationRequested;

    internal static CancellationLifetime CreateLinked(CancellationToken token) =>
        new(CancellationTokenSource.CreateLinkedTokenSource(token));

    internal void Cancel()
    {
        lock (_sync)
        {
            if (_disposeRequested) return;
            _cancelUsers++;
        }
        try { _source.Cancel(); }
        finally
        {
            lock (_sync)
            {
                _cancelUsers--;
                if (_disposeRequested && _cancelUsers == 0) _source.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposeRequested) return;
            _disposeRequested = true;
            if (_cancelUsers == 0) _source.Dispose();
        }
    }
}
