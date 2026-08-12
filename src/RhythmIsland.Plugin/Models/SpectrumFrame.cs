namespace RhythmIsland.Models;

public sealed class SpectrumFrame
{
    public SpectrumFrame(IEnumerable<float> bands, DateTimeOffset generatedAt, bool isSilent)
    {
        Bands = Array.AsReadOnly(bands.Select(x => float.IsFinite(x) ? Math.Clamp(x, 0f, 1f) : 0f).ToArray());
        GeneratedAt = generatedAt;
        IsSilent = isSilent;
    }

    public IReadOnlyList<float> Bands { get; }
    public DateTimeOffset GeneratedAt { get; }
    public bool IsSilent { get; }
    public float Peak => Bands.Count == 0 ? 0 : Bands.Max();
}
