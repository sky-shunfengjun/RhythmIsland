namespace RhythmIsland.Services;

internal sealed class CaptureFaultLatch
{
    private int _faulted;

    internal bool IsFaulted => Volatile.Read(ref _faulted) != 0;

    internal bool TryEnterFault() => Interlocked.Exchange(ref _faulted, 1) == 0;

    internal void Reset() => Interlocked.Exchange(ref _faulted, 0);
}
