namespace RhythmIsland.Abstractions;

public interface ISpectrumRenderClock
{
    IDisposable Subscribe(Action callback);
}

