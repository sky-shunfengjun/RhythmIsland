namespace RhythmIsland.Services;

internal sealed class CaptureInstanceGuard
{
    private long _nextInstanceId;
    private long _activeInstanceId;
    private object? _activeSource;

    internal long Begin(object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var instanceId = Interlocked.Increment(ref _nextInstanceId);
        Volatile.Write(ref _activeSource, source);
        Volatile.Write(ref _activeInstanceId, instanceId);
        return instanceId;
    }

    internal bool IsCurrent(long instanceId, object? source) =>
        instanceId != 0 &&
        instanceId == Volatile.Read(ref _activeInstanceId) &&
        ReferenceEquals(source, Volatile.Read(ref _activeSource));

    internal void Clear(long instanceId, object? source)
    {
        if (!IsCurrent(instanceId, source)) return;
        Volatile.Write(ref _activeInstanceId, 0);
        Volatile.Write(ref _activeSource, null);
    }
}
