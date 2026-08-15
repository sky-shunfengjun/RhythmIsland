using Avalonia;
using Avalonia.Controls;

namespace ClassIsland.Controls;

/// <summary>
/// Runtime-XAML test stand-in for the host-only control that is not part of the Plugin SDK output.
/// </summary>
public sealed class MainWindowLine : ContentControl
{
    public static readonly StyledProperty<bool> IsMainLineProperty =
        AvaloniaProperty.Register<MainWindowLine, bool>(nameof(IsMainLine));

    public bool IsMainLine
    {
        get => GetValue(IsMainLineProperty);
        set => SetValue(IsMainLineProperty, value);
    }
}
