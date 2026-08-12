using RhythmIsland.Models;

namespace RhythmIsland.Abstractions;

public interface ISpectrumFrameProvider
{
    SpectrumFrame? Latest { get; }
    void Publish(SpectrumFrame frame);
    void Clear();
}
