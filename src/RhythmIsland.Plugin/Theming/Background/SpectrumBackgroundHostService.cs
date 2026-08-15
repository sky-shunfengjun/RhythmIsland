using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using RhythmIsland.Abstractions;
using RhythmIsland.Services;
using RhythmIsland.Theming.Features;

namespace RhythmIsland.Theming.Background;

internal sealed class SpectrumBackgroundHostService
{
    private readonly RhythmIslandSettingsStore _settingsStore;
    private readonly ISpectrumFrameProvider _frames;
    private readonly ISpectrumRenderClock _renderClock;
    private readonly ISystemMediaCoverService _mediaCoverService;
    private readonly SpectrumDisplayCapabilityService _displayCapabilities;
    private readonly IReadOnlyList<IBackgroundThemeFeature> _features;
    private readonly BackgroundThemeStatus _status;
    private readonly ILogger<SpectrumBackgroundHostService> _logger;
    private readonly ConditionalWeakTable<Grid, SpectrumBackgroundControl> _mounted = new();
    private readonly ConditionalWeakTable<Border, SpectrumBackgroundControl> _legacyMounted = new();
    private readonly ConditionalWeakTable<Control, ThemePresenceEntry> _themePresence = new();
    private readonly ConditionalWeakTable<Control, ThemeProblemEntry> _themeProblems = new();
    private int _mountedCount;
    private int _legacyMountedCount;
    private int _themePresenceCount;
    private int _legacyThemePresenceCount;
    private bool _shutdown;

    public SpectrumBackgroundHostService(
        RhythmIslandSettingsStore settingsStore,
        ISpectrumFrameProvider frames,
        ISpectrumRenderClock renderClock,
        ISystemMediaCoverService mediaCoverService,
        SpectrumDisplayCapabilityService displayCapabilities,
        IEnumerable<IBackgroundThemeFeature> features,
        BackgroundThemeStatus status,
        ILogger<SpectrumBackgroundHostService> logger)
    {
        _settingsStore = settingsStore;
        _frames = frames;
        _renderClock = renderClock;
        _mediaCoverService = mediaCoverService;
        _displayCapabilities = displayCapabilities;
        _features = features.ToArray();
        _status = status;
        _logger = logger;
        _settingsStore.Settings.BackgroundSpectrum.PropertyChanged += OnBackgroundSettingsChanged;
    }

    public bool Attach(Grid host)
    {
        if (_shutdown) return false;
        if (_mounted.TryGetValue(host, out var existing))
        {
            if (host.Children.Contains(existing))
            {
                existing.SetEnabled(_settingsStore.Settings.BackgroundSpectrum.IsEnabled);
                UpdateStatus();
                return true;
            }

            existing.Release();
            _mounted.Remove(host);
            _mountedCount = Math.Max(0, _mountedCount - 1);
        }

        try
        {
            var control = CreateControl();
            host.Children.Insert(0, control);
            host.DetachedFromVisualTree += OnHostDetachedFromVisualTree;
            _mounted.Add(host, control);
            _mountedCount++;
            ClearProblem(host);
            UpdateStatus();
            if (_mountedCount == 1)
                _logger.LogInformation("配套背景主题已通过 v2 独立层挂载律动岛背景频谱。");
            return true;
        }
        catch (Exception exception)
        {
            ReportIncompatible(host, "无法在 Grid#PART_GridWrapper 中插入背景频谱层。");
            UpdateStatus();
            _logger.LogError(exception, "挂载律动岛独立背景频谱层失败。");
            return false;
        }
    }

    public bool AttachLegacy(Border border)
    {
        if (_shutdown) return false;
        if (_legacyMounted.TryGetValue(border, out var existing))
        {
            if (ReferenceEquals(border.Child, existing))
            {
                existing.SetEnabled(_settingsStore.Settings.BackgroundSpectrum.IsEnabled);
                UpdateStatus();
                return true;
            }

            existing.Release();
            _legacyMounted.Remove(border);
            _legacyMountedCount = Math.Max(0, _legacyMountedCount - 1);
        }

        if (border.Child is not null)
        {
            ReportIncompatible(border, "v1 目标背景已有内容。");
            _logger.LogWarning("检测到 v1 配套背景主题，但目标背景已有内容，律动岛没有覆盖它。");
            return false;
        }

        try
        {
            var control = CreateControl();
            border.Child = control;
            border.DetachedFromVisualTree += OnHostDetachedFromVisualTree;
            _legacyMounted.Add(border, control);
            _legacyMountedCount++;
            ClearProblem(border);
            UpdateStatus();
            if (_legacyMountedCount == 1)
                _logger.LogWarning("正在兼容旧版 v1 配套主题；请更新主题以获得独立背景透明度。");
            return true;
        }
        catch (Exception exception)
        {
            ReportIncompatible(border, "无法挂载旧版 v1 背景频谱层。");
            UpdateStatus();
            _logger.LogError(exception, "挂载旧版律动岛背景频谱失败。");
            return false;
        }
    }

    internal bool IsMounted(Control control) => control switch
    {
        Grid grid => _mounted.TryGetValue(grid, out _),
        Border border => _legacyMounted.TryGetValue(border, out _),
        _ => false
    };

    internal void RegisterThemePresence(Control control, int contractVersion)
    {
        if (_shutdown) return;
        if (_themePresence.TryGetValue(control, out var existing))
        {
            if (existing.ContractVersion == contractVersion)
            {
                HideMissingPluginNotice(control);
                return;
            }
            RemoveThemePresence(control, existing);
        }

        var entry = new ThemePresenceEntry(contractVersion);
        _themePresence.Add(control, entry);
        control.DetachedFromVisualTree += OnThemeControlDetachedFromVisualTree;
        _themePresenceCount++;
        if (contractVersion == SpectrumThemeBridge.LegacyContractVersion) _legacyThemePresenceCount++;
        HideMissingPluginNotice(control);
        ClearProblem(control);
        UpdateStatus();
        if (_themePresenceCount == 1)
            _logger.LogInformation("已检测到律动岛配套背景主题 v{ContractVersion} 标记。", contractVersion);
    }

    internal void RemoveControl(Control control)
    {
        switch (control)
        {
            case Grid grid when IsMounted(grid):
                Detach(grid);
                break;
            case Border border when IsMounted(border):
                DetachLegacy(border);
                break;
        }

        if (_themePresence.TryGetValue(control, out var presence)) RemoveThemePresence(control, presence);
        ClearProblem(control);
    }

    public void Detach(Grid host)
    {
        if (!_mounted.TryGetValue(host, out var control)) return;
        var hadAnyMount = TotalMountedCount > 0;
        host.DetachedFromVisualTree -= OnHostDetachedFromVisualTree;
        host.Children.Remove(control);
        control.Release();
        _mounted.Remove(host);
        _mountedCount = Math.Max(0, _mountedCount - 1);
        UpdateStatus();
        if (hadAnyMount && TotalMountedCount == 0)
            _logger.LogInformation("律动岛背景频谱已从配套主题卸载。");
    }

    public void DetachLegacy(Border border)
    {
        if (!_legacyMounted.TryGetValue(border, out var control)) return;
        var hadAnyMount = TotalMountedCount > 0;
        border.DetachedFromVisualTree -= OnHostDetachedFromVisualTree;
        if (ReferenceEquals(border.Child, control)) border.Child = null;
        control.Release();
        _legacyMounted.Remove(border);
        _legacyMountedCount = Math.Max(0, _legacyMountedCount - 1);
        UpdateStatus();
        if (hadAnyMount && TotalMountedCount == 0)
            _logger.LogInformation("律动岛背景频谱已从配套主题卸载。");
    }

    public void ReportContractMismatch(Control control, string requestedVersion)
    {
        var shouldLog = !HasProblem(control, ThemeProblemKind.ContractMismatch, requestedVersion);
        SetProblem(control, new ThemeProblemEntry(ThemeProblemKind.ContractMismatch, requestedVersion));
        UpdateStatus();
        if (shouldLog)
        {
            _logger.LogWarning(
                "律动岛配套主题桥接版本不匹配：主题请求 {RequestedVersion}，插件支持 v1 和 v{SupportedVersion}。",
                requestedVersion,
                SpectrumThemeBridge.CurrentContractVersion);
        }
    }

    public void ReportIncompatible(Control control, string reason)
    {
        var shouldLog = !HasProblem(control, ThemeProblemKind.Incompatible, reason);
        SetProblem(control, new ThemeProblemEntry(ThemeProblemKind.Incompatible, reason));
        UpdateStatus();
        if (shouldLog) _logger.LogWarning("当前配套主题结构不兼容：{Reason}", reason);
    }

    internal void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;
        _settingsStore.Settings.BackgroundSpectrum.PropertyChanged -= OnBackgroundSettingsChanged;
        foreach (var pair in _mounted.ToArray()) Detach(pair.Key);
        foreach (var pair in _legacyMounted.ToArray()) DetachLegacy(pair.Key);
        foreach (var pair in _themePresence.ToArray()) RemoveThemePresence(pair.Key, pair.Value);
        foreach (var pair in _themeProblems.ToArray()) ClearProblem(pair.Key);
        _themePresenceCount = 0;
        _legacyThemePresenceCount = 0;
        UpdateStatus();
    }

    private int TotalMountedCount => _mountedCount + _legacyMountedCount;

    private SpectrumBackgroundControl CreateControl()
    {
        var control = new SpectrumBackgroundControl(
            _settingsStore.Settings.BackgroundSpectrum,
            _frames,
            _renderClock,
            _mediaCoverService,
            _displayCapabilities,
            _features,
            _logger);
        control.SetEnabled(_settingsStore.Settings.BackgroundSpectrum.IsEnabled);
        return control;
    }

    private void RemoveThemePresence(Control control, ThemePresenceEntry entry)
    {
        control.DetachedFromVisualTree -= OnThemeControlDetachedFromVisualTree;
        _themePresence.Remove(control);
        RestoreMissingPluginNotice(control);
        _themePresenceCount = Math.Max(0, _themePresenceCount - 1);
        if (entry.ContractVersion == SpectrumThemeBridge.LegacyContractVersion)
            _legacyThemePresenceCount = Math.Max(0, _legacyThemePresenceCount - 1);
        UpdateStatus();
    }

    private static void HideMissingPluginNotice(Control control)
    {
        control.SetValue(ToolTip.TipProperty, null);
        control.SetValue(ToolTip.IsOpenProperty, false);
    }

    private static void RestoreMissingPluginNotice(Control control)
    {
        control.ClearValue(ToolTip.TipProperty);
        control.ClearValue(ToolTip.IsOpenProperty);
    }

    private void OnBackgroundSettingsChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName != nameof(Models.SpectrumBackgroundSettings.IsEnabled)) return;
        var enabled = _settingsStore.Settings.BackgroundSpectrum.IsEnabled;
        foreach (var pair in _mounted) pair.Value.SetEnabled(enabled);
        foreach (var pair in _legacyMounted) pair.Value.SetEnabled(enabled);
        UpdateStatus();
    }

    private void OnHostDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs eventArgs)
    {
        switch (sender)
        {
            case Grid grid:
                Detach(grid);
                break;
            case Border border:
                DetachLegacy(border);
                break;
        }
    }

    private void OnThemeControlDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs eventArgs)
    {
        if (sender is Control control) RemoveControl(control);
    }

    private void UpdateStatus()
    {
        if (_mountedCount > 0)
        {
            SetMountedStatus(false);
            return;
        }

        if (_legacyMountedCount > 0)
        {
            SetMountedStatus(true);
            return;
        }

        var mismatch = _themeProblems
            .Select(pair => pair.Value)
            .FirstOrDefault(problem => problem.Kind == ThemeProblemKind.ContractMismatch);
        if (mismatch is not null)
        {
            _status.Set(BackgroundThemeState.ContractMismatch,
                $"配套主题桥接版本不匹配（主题请求 {mismatch.Detail}，插件支持 v1 和 v{SpectrumThemeBridge.CurrentContractVersion}）。");
            return;
        }

        if (_themeProblems.Any(pair => pair.Value.Kind == ThemeProblemKind.Incompatible))
        {
            _status.Set(BackgroundThemeState.Incompatible, "当前主题结构不兼容，律动岛没有覆盖原有界面内容。");
            return;
        }

        if (_themePresenceCount > 0)
        {
            if (!_settingsStore.Settings.BackgroundSpectrum.IsEnabled)
                _status.Set(BackgroundThemeState.DisabledByUser, "配套主题已启用，背景频谱已关闭。");
            else if (_legacyThemePresenceCount == _themePresenceCount)
                _status.Set(BackgroundThemeState.EnabledWaiting, "旧版配套主题已启用，正在等待主要行背景；建议更新主题以支持独立透明度。");
            else
                _status.Set(BackgroundThemeState.EnabledWaiting, "配套主题已启用，正在等待主要行背景；请先设置一个主要行。");
            return;
        }

        _status.Set(BackgroundThemeState.NotEnabled, "未启用配套背景主题。");
    }

    private void SetMountedStatus(bool legacy)
    {
        if (!_settingsStore.Settings.BackgroundSpectrum.IsEnabled)
        {
            _status.Set(BackgroundThemeState.DisabledByUser, "配套主题已启用，背景频谱已关闭。");
            return;
        }

        _status.Set(
            BackgroundThemeState.Displaying,
            legacy
                ? "配套背景主题正在显示频谱；当前主题版本较旧，建议更新以支持独立透明度。"
                : "配套背景主题正在主要行显示频谱。");
    }

    internal void ClearProblem(Control control)
    {
        if (!_themeProblems.TryGetValue(control, out _)) return;
        control.DetachedFromVisualTree -= OnProblemControlDetachedFromVisualTree;
        _themeProblems.Remove(control);
        UpdateStatus();
    }

    private bool HasProblem(Control control, ThemeProblemKind kind, string detail) =>
        _themeProblems.TryGetValue(control, out var existing) &&
        existing.Kind == kind && string.Equals(existing.Detail, detail, StringComparison.Ordinal);

    private void SetProblem(Control control, ThemeProblemEntry problem)
    {
        if (_themeProblems.TryGetValue(control, out _))
        {
            control.DetachedFromVisualTree -= OnProblemControlDetachedFromVisualTree;
            _themeProblems.Remove(control);
        }
        _themeProblems.Add(control, problem);
        control.DetachedFromVisualTree += OnProblemControlDetachedFromVisualTree;
    }

    private void OnProblemControlDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs eventArgs)
    {
        if (sender is Control control) ClearProblem(control);
    }

    private sealed record ThemePresenceEntry(int ContractVersion);
    private sealed record ThemeProblemEntry(ThemeProblemKind Kind, string Detail);
    private enum ThemeProblemKind { Incompatible, ContractMismatch }
}
