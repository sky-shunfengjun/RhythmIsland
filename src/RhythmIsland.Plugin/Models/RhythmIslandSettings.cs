using CommunityToolkit.Mvvm.ComponentModel;

namespace RhythmIsland.Models;

public enum SpectrumDisplayMode { BottomUp, Centered }
public enum SpectrumVisualizationStyle { Bars, SmoothLine, FilledCurve }
public enum SpectrumFrequencyBalanceMode { Original, Balanced, HighBoost }

public sealed class RhythmIslandSettings : ObservableObject
{
    internal static readonly int[] AllowedBarCounts = [24, 32, 48, 64, 96];

    private bool _isEnabled;
    private double _sensitivity = 1.0;
    private double _smoothing = 0.65;
    private SpectrumBackgroundSettings _backgroundSpectrum = new();

    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
    public double Sensitivity { get => _sensitivity; set => SetProperty(ref _sensitivity, ClampFinite(value, 0.50, 3.00, 1.00)); }
    public double Smoothing { get => _smoothing; set => SetProperty(ref _smoothing, ClampFinite(value, 0.00, 1.00, 0.65)); }
    public SpectrumBackgroundSettings BackgroundSpectrum
    {
        get => _backgroundSpectrum;
        set => SetProperty(ref _backgroundSpectrum, value ?? new SpectrumBackgroundSettings());
    }

    internal void Validate()
    {
        Sensitivity = Sensitivity;
        Smoothing = Smoothing;
        BackgroundSpectrum ??= new SpectrumBackgroundSettings();
        BackgroundSpectrum.Validate();
    }

    private static double ClampFinite(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
