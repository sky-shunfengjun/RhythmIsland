namespace RhythmIsland.Models;

public sealed class SpectrumBackgroundSettings : SpectrumVisualSettings
{
    private bool _isEnabled = true;
    private bool _isFixedWidthEnabled = true;
    private double _fixedWidth = 240;

    public SpectrumBackgroundSettings()
    {
        Opacity = 0.80;
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public bool IsFixedWidthEnabled
    {
        get => _isFixedWidthEnabled;
        set => SetProperty(ref _isFixedWidthEnabled, value);
    }

    public double FixedWidth
    {
        get => _fixedWidth;
        set => SetProperty(ref _fixedWidth,
            double.IsFinite(value) ? Math.Clamp(value, 120, 1920) : 240);
    }

    internal override void Validate()
    {
        base.Validate();
        FixedWidth = FixedWidth;
    }
}
