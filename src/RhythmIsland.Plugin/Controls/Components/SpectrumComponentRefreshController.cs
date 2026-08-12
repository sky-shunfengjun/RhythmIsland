using RhythmIsland.Abstractions;

namespace RhythmIsland.Controls.Components;

internal sealed class SpectrumComponentRefreshController(ISpectrumRenderClock clock, Action invalidate) : IDisposable
{
    private IDisposable? _subscription;
    internal bool IsAttached => _subscription is not null;

    internal void Attach() => _subscription ??= clock.Subscribe(invalidate);

    internal void Detach()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    public void Dispose() => Detach();
}

