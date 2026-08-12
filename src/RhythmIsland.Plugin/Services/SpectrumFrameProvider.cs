using RhythmIsland.Abstractions;
using RhythmIsland.Models;

namespace RhythmIsland.Services;

public sealed class SpectrumFrameProvider : ISpectrumFrameProvider
{
    private SpectrumFrame? _latest;
    public SpectrumFrame? Latest => Volatile.Read(ref _latest);
    public void Publish(SpectrumFrame frame) => Volatile.Write(ref _latest, frame);
    public void Clear() => Volatile.Write(ref _latest, null);
}
