using Avalonia;
using Avalonia.Controls;
using RhythmIsland.Models;

namespace RhythmIsland.Controls.Settings;

public partial class SpectrumAppearanceSettingsControl : UserControl
{
    public static readonly StyledProperty<SpectrumVisualSettings?> SettingsProperty =
        AvaloniaProperty.Register<SpectrumAppearanceSettingsControl, SpectrumVisualSettings?>(nameof(Settings));

    public SpectrumAppearanceSettingsControl() => InitializeComponent();

    public SpectrumVisualSettings? Settings
    {
        get => GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }
  }
