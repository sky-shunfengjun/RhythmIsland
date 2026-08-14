using System.Text.Json.Serialization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RhythmIsland.Services;

namespace RhythmIsland.Models;

public sealed record SpectrumFrameRateOption(int Value, string DisplayName);

public sealed class SpectrumComponentSettings : ObservableObject, IJsonOnDeserialized
{
    internal static readonly int[] AllowedFrameRates = [0, 30, 60, 90, 120];
    private SpectrumVisualizationStyle _visualizationStyle = SpectrumVisualizationStyle.Bars;
    private SpectrumDisplayMode _displayMode = SpectrumDisplayMode.BottomUp;
    private SpectrumColorSource _colorSource = SpectrumColorSource.ThemeAccent;
    private bool? _legacyUseCustomColor;
    private bool _hasExplicitColorSource;
    private string _customColorText = "#FF4DA3FF";
    private SpectrumMediaColorMode _mediaCoverColorMode = SpectrumMediaColorMode.Vivid;
    private SpectrumGradientMode _gradientMode = SpectrumGradientMode.Off;
    private SpectrumGradientSpeed _gradientSpeed = SpectrumGradientSpeed.Medium;
    private bool? _legacyGradientEnabled;
    private bool _hasExplicitGradientMode;
    private bool _useCustomGradientEndColor;
    private string _gradientEndColorText = "#FF9B5DE5";
    private bool _glowEnabled;
    private double _glowIntensity = 0.50;
    private int _frameRate = 30;
    private int _barCount = 48;
    private bool _horizontalMirrorEnabled = true;
    private SpectrumFrequencyBalanceMode _frequencyBalanceMode = SpectrumFrequencyBalanceMode.Balanced;
    private double _amplitude = 1.0;
    private double _opacity = 1.00;
    private double _width = 240;
    private bool _autoCollapseEnabled = true;
    private double _silenceCollapseDelaySeconds = 5;
    private string _mediaCoverStatusText = "选择音乐封面后显示获取状态。";
    private IReadOnlyList<SpectrumFrameRateOption> _availableFrameRates =
        SpectrumFrameRateOptions.ForRefreshRate(null);

    public SpectrumVisualizationStyle VisualizationStyle
    {
        get => _visualizationStyle;
        set
        {
            var validated = Enum.IsDefined(value) ? value : SpectrumVisualizationStyle.Bars;
            if (!SetProperty(ref _visualizationStyle, validated)) return;
            OnPropertyChanged(nameof(VisualizationStyleIndex));
        }
    }

    [JsonIgnore]
    public int VisualizationStyleIndex
    {
        get => (int)VisualizationStyle;
        set => VisualizationStyle = value is >= 0 and <= 2
            ? (SpectrumVisualizationStyle)value
            : SpectrumVisualizationStyle.Bars;
    }

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

    public SpectrumColorSource ColorSource
    {
        get => _colorSource;
        set
        {
            _hasExplicitColorSource = true;
            var validated = Enum.IsDefined(value) ? value : SpectrumColorSource.ThemeAccent;
            if (!SetProperty(ref _colorSource, validated)) return;
            OnColorSourceChanged();
        }
    }

    [JsonIgnore]
    public int ColorSourceIndex
    {
        get => (int)ColorSource;
        set => ColorSource = value is >= 0 and <= 2 ? (SpectrumColorSource)value : SpectrumColorSource.ThemeAccent;
    }

    [JsonIgnore]
    public bool UseCustomColor
    {
        get => ColorSource == SpectrumColorSource.Custom;
        set => ColorSource = value ? SpectrumColorSource.Custom : SpectrumColorSource.ThemeAccent;
    }

    [JsonPropertyName(nameof(UseCustomColor))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyUseCustomColor
    {
        get => null;
        set => _legacyUseCustomColor = value;
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

    public SpectrumGradientMode GradientMode
    {
        get => _gradientMode;
        set
        {
            _hasExplicitGradientMode = true;
            var validated = Enum.IsDefined(value) ? value : SpectrumGradientMode.Off;
            if (!SetProperty(ref _gradientMode, validated)) return;
            OnGradientModeChanged();
        }
    }

    public SpectrumMediaColorMode MediaCoverColorMode
    {
        get => _mediaCoverColorMode;
        set
        {
            var validated = Enum.IsDefined(value) ? value : SpectrumMediaColorMode.Vivid;
            if (!SetProperty(ref _mediaCoverColorMode, validated)) return;
            OnPropertyChanged(nameof(MediaCoverColorModeIndex));
        }
    }

    [JsonIgnore]
    public int MediaCoverColorModeIndex
    {
        get => (int)MediaCoverColorMode;
        set => MediaCoverColorMode = value is >= 0 and <= 2
            ? (SpectrumMediaColorMode)value
            : SpectrumMediaColorMode.Vivid;
    }

    [JsonIgnore]
    public int GradientModeIndex
    {
        get => (int)GradientMode;
        set => GradientMode = value is >= 0 and <= 2 ? (SpectrumGradientMode)value : SpectrumGradientMode.Off;
    }

    [JsonIgnore]
    public bool GradientEnabled
    {
        get => GradientMode != SpectrumGradientMode.Off;
        set => GradientMode = value ? SpectrumGradientMode.Static : SpectrumGradientMode.Off;
    }

    [JsonPropertyName(nameof(GradientEnabled))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyGradientEnabled
    {
        get => null;
        set => _legacyGradientEnabled = value;
    }

    public SpectrumGradientSpeed GradientSpeed
    {
        get => _gradientSpeed;
        set
        {
            var validated = Enum.IsDefined(value) ? value : SpectrumGradientSpeed.Medium;
            if (!SetProperty(ref _gradientSpeed, validated)) return;
            OnPropertyChanged(nameof(GradientSpeedIndex));
        }
    }

    [JsonIgnore]
    public int GradientSpeedIndex
    {
        get => (int)GradientSpeed;
        set => GradientSpeed = value is >= 0 and <= 2 ? (SpectrumGradientSpeed)value : SpectrumGradientSpeed.Medium;
    }

    public bool UseCustomGradientEndColor
    {
        get => _useCustomGradientEndColor;
        set => SetProperty(ref _useCustomGradientEndColor, value);
    }

    [JsonIgnore]
    public Color GradientEndColor
    {
        get => Color.Parse(_gradientEndColorText);
        set
        {
            var text = value.ToString().ToUpperInvariant();
            if (!SetProperty(ref _gradientEndColorText, text, nameof(GradientEndColorText))) return;
            OnPropertyChanged();
        }
    }

    [JsonPropertyName(nameof(GradientEndColor))]
    public string GradientEndColorText
    {
        get => _gradientEndColorText;
        set
        {
            var normalized = Color.TryParse(value, out var parsed) ? parsed.ToString().ToUpperInvariant() : "#FF9B5DE5";
            if (!SetProperty(ref _gradientEndColorText, normalized)) return;
            OnPropertyChanged(nameof(GradientEndColor));
        }
    }

    public bool GlowEnabled
    {
        get => _glowEnabled;
        set => SetProperty(ref _glowEnabled, value);
    }

    public double GlowIntensity
    {
        get => _glowIntensity;
        set => SetProperty(ref _glowIntensity,
            double.IsFinite(value) ? Math.Clamp(value, 0.10, 1.00) : 0.50);
    }

    public int FrameRate
    {
        get => _frameRate;
        set => SetProperty(ref _frameRate,
            AllowedFrameRates.Contains(value) ? value : 30);
    }

    public int BarCount
    {
        get => _barCount;
        set
        {
            SetProperty(ref _barCount, RhythmIslandSettings.AllowedBarCounts.Contains(value) ? value : 48);
        }
    }

    public bool HorizontalMirrorEnabled
    {
        get => _horizontalMirrorEnabled;
        set => SetProperty(ref _horizontalMirrorEnabled, value);
    }

    public SpectrumFrequencyBalanceMode FrequencyBalanceMode
    {
        get => _frequencyBalanceMode;
        set
        {
            var validated = Enum.IsDefined(value) ? value : SpectrumFrequencyBalanceMode.Balanced;
            if (!SetProperty(ref _frequencyBalanceMode, validated)) return;
            OnPropertyChanged(nameof(FrequencyBalanceModeIndex));
        }
    }

    [JsonIgnore]
    public int FrequencyBalanceModeIndex
    {
        get => (int)FrequencyBalanceMode;
        set => FrequencyBalanceMode = value is >= 0 and <= 2
            ? (SpectrumFrequencyBalanceMode)value
            : SpectrumFrequencyBalanceMode.Balanced;
    }

    public double Amplitude
    {
        get => _amplitude;
        set => SetProperty(ref _amplitude, double.IsFinite(value) ? Math.Clamp(value, 0.25, 3.00) : 1.00);
    }

    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, double.IsFinite(value) ? Math.Clamp(value, 0.10, 1.00) : 1.00);
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
    public IReadOnlyList<SpectrumFrameRateOption> AvailableFrameRates => _availableFrameRates;

    internal void SetAvailableFrameRates(IReadOnlyList<SpectrumFrameRateOption> options) =>
        SetProperty(ref _availableFrameRates, options, nameof(AvailableFrameRates));

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

    [JsonIgnore]
    public bool IsThemeColorSource => ColorSource == SpectrumColorSource.ThemeAccent;

    [JsonIgnore]
    public bool IsMediaCoverColorSource => ColorSource == SpectrumColorSource.MediaCover;

    [JsonIgnore]
    public bool IsCustomColorSource => ColorSource == SpectrumColorSource.Custom;

    [JsonIgnore]
    public bool IsGradientActive => GradientMode != SpectrumGradientMode.Off;

    [JsonIgnore]
    public bool IsDynamicGradient => GradientMode == SpectrumGradientMode.Dynamic;

    [JsonIgnore]
    public bool CanCustomizeGradientEnd => IsGradientActive && !IsMediaCoverColorSource;

    [JsonIgnore]
    public string MediaCoverStatusText => _mediaCoverStatusText;

    internal void SetMediaCoverStatusText(string value) =>
        SetProperty(ref _mediaCoverStatusText, value, nameof(MediaCoverStatusText));

    public void OnDeserialized()
    {
        if (!_hasExplicitColorSource && _legacyUseCustomColor.HasValue)
        {
            _colorSource = _legacyUseCustomColor.Value
                ? SpectrumColorSource.Custom
                : SpectrumColorSource.ThemeAccent;
        }

        if (!_hasExplicitGradientMode && _legacyGradientEnabled.HasValue)
            _gradientMode = _legacyGradientEnabled.Value ? SpectrumGradientMode.Static : SpectrumGradientMode.Off;

        _legacyUseCustomColor = null;
        _legacyGradientEnabled = null;
        OnColorSourceChanged();
        OnGradientModeChanged();
    }

    private void OnColorSourceChanged()
    {
        OnPropertyChanged(nameof(ColorSourceIndex));
        OnPropertyChanged(nameof(UseCustomColor));
        OnPropertyChanged(nameof(IsThemeColorSource));
        OnPropertyChanged(nameof(IsMediaCoverColorSource));
        OnPropertyChanged(nameof(IsCustomColorSource));
        OnPropertyChanged(nameof(CanCustomizeGradientEnd));
    }

    private void OnGradientModeChanged()
    {
        OnPropertyChanged(nameof(GradientModeIndex));
        OnPropertyChanged(nameof(GradientEnabled));
        OnPropertyChanged(nameof(IsGradientActive));
        OnPropertyChanged(nameof(IsDynamicGradient));
        OnPropertyChanged(nameof(CanCustomizeGradientEnd));
    }
}
