using System.Text.Json.Serialization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RhythmIsland.Models;

public sealed class SpectrumComponentSettings : ObservableObject
{
    private SpectrumDisplayMode _displayMode = SpectrumDisplayMode.BottomUp;
    private bool _useCustomColor;
    private string _customColorText = "#FF4DA3FF";
    private int _barCount = 48;
    private double _amplitude = 1.0;
    private double _opacity = 0.65;
    private double _width = 240;
    private bool _autoCollapseEnabled = true;
    private double _silenceCollapseDelaySeconds = 5;

    public SpectrumDisplayMode DisplayMode
    {
        get => _displayMode;
        set
        {
            var validated = Enum.IsDefined(value) ? value : SpectrumDisplayMode.BottomUp;
            if (!SetProperty(ref _displayMode, validated)) return;
            OnPropertyChanged(nameof(IsBottomUpMode));
            OnPropertyChanged(nameof(IsCenteredMode));
        }
    }

    public bool UseCustomColor
    {
        get => _useCustomColor;
        set => SetProperty(ref _useCustomColor, value);
    }

    [JsonIgnore]
    public Color CustomColor
    {
        get => Color.Parse(_customColorText);
        set
        {
            var text = value.ToString().ToUpperInvariant();
            if (!SetProperty(ref _customColorText, text, nameof(CustomColorText))) return;
            OnPropertyChanged();
        }
    }

    [JsonPropertyName(nameof(CustomColor))]
    public string CustomColorText
    {
        get => _customColorText;
        set
        {
            var normalized = Color.TryParse(value, out var parsed) ? parsed.ToString().ToUpperInvariant() : "#FF4DA3FF";
            if (!SetProperty(ref _customColorText, normalized)) return;
            OnPropertyChanged(nameof(CustomColor));
        }
    }

    public int BarCount
    {
        get => _barCount;
        set
        {
            SetProperty(ref _barCount, RhythmIslandSettings.AllowedBarCounts.Contains(value) ? value : 48);
        }
    }

    public double Amplitude
    {
        get => _amplitude;
        set => SetProperty(ref _amplitude, double.IsFinite(value) ? Math.Clamp(value, 0.25, 3.00) : 1.00);
    }

    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, double.IsFinite(value) ? Math.Clamp(value, 0.10, 1.00) : 0.65);
    }

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

    [JsonIgnore]
    public IReadOnlyList<int> AllowedBarCounts { get; } = RhythmIslandSettings.AllowedBarCounts;

    [JsonIgnore]
    public bool IsBottomUpMode
    {
        get => DisplayMode == SpectrumDisplayMode.BottomUp;
        set { if (value) DisplayMode = SpectrumDisplayMode.BottomUp; }
    }

    [JsonIgnore]
    public bool IsCenteredMode
    {
        get => DisplayMode == SpectrumDisplayMode.Centered;
        set { if (value) DisplayMode = SpectrumDisplayMode.Centered; }
    }
}
