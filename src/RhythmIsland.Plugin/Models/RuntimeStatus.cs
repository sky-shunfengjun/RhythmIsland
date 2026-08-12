using CommunityToolkit.Mvvm.ComponentModel;

namespace RhythmIsland.Models;

public enum RuntimeState
{
    Stopped,
    Starting,
    Running,
    DeviceUnavailable,
    Faulted
}

public sealed partial class RuntimeStatus : ObservableObject
{
    [ObservableProperty] private RuntimeState _state = RuntimeState.Stopped;
    [ObservableProperty] private string _deviceName = "未连接";
    [ObservableProperty] private DateTimeOffset? _lastFrameAt;
    [ObservableProperty] private float _peak;
    [ObservableProperty] private string _lastError = "无";

    public string StateDisplay => State switch
    {
        RuntimeState.Stopped => "已停止",
        RuntimeState.Starting => "正在启动",
        RuntimeState.Running => "运行中",
        RuntimeState.DeviceUnavailable => "没有可用的输出设备",
        RuntimeState.Faulted => "发生错误",
        _ => "未知"
    };

    public string LastFrameDisplay => LastFrameAt?.ToLocalTime().ToString("HH:mm:ss.fff") ?? "尚未收到";
    public string PeakDisplay => $"{Math.Clamp(Peak, 0, 1):P1}";

    partial void OnStateChanged(RuntimeState value) => OnPropertyChanged(nameof(StateDisplay));
    partial void OnLastFrameAtChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(LastFrameDisplay));
    partial void OnPeakChanged(float value) => OnPropertyChanged(nameof(PeakDisplay));
}
