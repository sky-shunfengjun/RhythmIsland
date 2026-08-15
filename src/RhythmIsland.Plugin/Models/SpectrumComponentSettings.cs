namespace RhythmIsland.Models;

public sealed class SpectrumComponentSettings : SpectrumVisualSettings
{
    private double _width = 240;
    private bool _autoCollapseEnabled = true;
    private double _silenceCollapseDelaySeconds = 5;

    public double Width
    {
        get => _width;
        set => SetProperty(ref _width, double.IsFinite(value) ? Math.Clamp(value, 60, 1200) : 240);
    }

    public bool AutoCollapseEnabled
    {
        get => _autoCollapseEnabled;
        set => SetProperty(ref _autoCollapseEnabled, value);
    }

    public double SilenceCollapseDelaySeconds
    {
        get => _silenceCollapseDelaySeconds;
        set => SetProperty(ref _silenceCollapseDelaySeconds,
            double.IsFinite(value) ? Math.Clamp(value, 1, 120) : 5);
    }

    internal override void Validate()
    {
        base.Validate();
        Width = Width;
        SilenceCollapseDelaySeconds = SilenceCollapseDelaySeconds;
    }
}
