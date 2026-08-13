using RhythmIsland.Models;

namespace RhythmIsland.Abstractions;

public interface ISystemMediaCoverService
{
    event EventHandler? Changed;
    SpectrumPalette? CurrentPalette { get; }
    SystemMediaCoverStatus Status { get; }
    string StatusText { get; }
    IDisposable Acquire();
}
