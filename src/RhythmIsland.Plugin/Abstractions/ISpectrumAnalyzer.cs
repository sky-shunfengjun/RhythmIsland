using RhythmIsland.Models;

namespace RhythmIsland.Abstractions;

public interface ISpectrumAnalyzer
{
    event EventHandler<SpectrumFrame>? FrameProduced;
    void Configure(int sampleRate, int barCount, double sensitivity, double smoothing);
    void PushSamples(ReadOnlySpan<float> samples);
    void AdvanceSilence();
    void Reset();
}
