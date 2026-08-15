using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using RhythmIsland.Theming.Background;

namespace RhythmIsland.Theming;

/// <summary>
/// 监听配套主题写入 Control.Tag 的主题存在标记与背景挂载标记。
/// </summary>
internal static class SpectrumThemeBridge
{
    public const int LegacyContractVersion = 1;
    public const int CurrentContractVersion = 2;
    public const string ThemeMarkerPrefix = "RhythmIsland.BackgroundTheme/";
    public const string LegacyThemeMarker = ThemeMarkerPrefix + "v1";
    public const string CurrentThemeMarker = ThemeMarkerPrefix + "v2";
    public const string BridgeMarkerPrefix = "RhythmIsland.BackgroundBridge/";
    public const string LegacyBridgeMarker = BridgeMarkerPrefix + "v1";
    public const string CurrentBridgeMarker = BridgeMarkerPrefix + "v2";

    private static readonly object Sync = new();
    private static readonly List<WeakReference<Control>> PendingControls = [];
    private static SpectrumBackgroundHostService? _service;
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0) return;
        Control.TagProperty.Changed.AddClassHandler<Control>((control, _) => Refresh(control));
    }

    internal static void Bind(SpectrumBackgroundHostService service)
    {
        List<WeakReference<Control>> pending;
        lock (Sync)
        {
            _service = service;
            pending = [.. PendingControls];
            PendingControls.Clear();
        }

        foreach (var reference in pending)
        {
            if (!reference.TryGetTarget(out var control)) continue;
            Dispatcher.UIThread.Post(() => Refresh(control), DispatcherPriority.Loaded);
        }
    }

    internal static void Unbind(SpectrumBackgroundHostService service)
    {
        lock (Sync)
        {
            if (ReferenceEquals(_service, service)) _service = null;
            PendingControls.Clear();
        }
        service.Shutdown();
    }

    private static void Refresh(Control control)
    {
        SpectrumBackgroundHostService? service;
        lock (Sync) service = _service;
        if (service is not null)
        {
            ApplyMarker(control, service);
            return;
        }

        if (!IsRhythmIslandMarker(control.Tag as string)) return;
        lock (Sync)
        {
            if (PendingControls.Any(reference => reference.TryGetTarget(out var existing) && ReferenceEquals(existing, control)))
                return;
            PendingControls.Add(new WeakReference<Control>(control));
        }
    }

    internal static void ApplyMarker(Control control, SpectrumBackgroundHostService service)
    {
        var marker = control.Tag as string;
        if (string.Equals(marker, CurrentThemeMarker, StringComparison.Ordinal))
        {
            service.ClearProblem(control);
            service.RegisterThemePresence(control, CurrentContractVersion);
            return;
        }

        if (string.Equals(marker, LegacyThemeMarker, StringComparison.Ordinal))
        {
            service.ClearProblem(control);
            service.RegisterThemePresence(control, LegacyContractVersion);
            return;
        }

        if (string.Equals(marker, CurrentBridgeMarker, StringComparison.Ordinal))
        {
            service.ClearProblem(control);
            if (control is Grid grid) service.Attach(grid);
            else
            {
                service.RemoveControl(control);
                service.ReportIncompatible(control, "v2 背景桥接标记没有应用到 Grid#PART_GridWrapper。");
            }
            return;
        }

        if (string.Equals(marker, LegacyBridgeMarker, StringComparison.Ordinal))
        {
            service.ClearProblem(control);
            if (control is Border border) service.AttachLegacy(border);
            else
            {
                service.RemoveControl(control);
                service.ReportIncompatible(control, "v1 背景桥接标记没有应用到 Border#BackgroundBorder。");
            }
            return;
        }

        if (marker?.StartsWith(ThemeMarkerPrefix, StringComparison.Ordinal) == true ||
            marker?.StartsWith(BridgeMarkerPrefix, StringComparison.Ordinal) == true)
        {
            service.RemoveControl(control);
            var separator = marker.LastIndexOf('/');
            service.ReportContractMismatch(control, separator >= 0 ? marker[(separator + 1)..] : marker);
            return;
        }

        service.RemoveControl(control);
    }

    private static bool IsRhythmIslandMarker(string? marker) =>
        marker?.StartsWith(ThemeMarkerPrefix, StringComparison.Ordinal) == true ||
        marker?.StartsWith(BridgeMarkerPrefix, StringComparison.Ordinal) == true;
}
