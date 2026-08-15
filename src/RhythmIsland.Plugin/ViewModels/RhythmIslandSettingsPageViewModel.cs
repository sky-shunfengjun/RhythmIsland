using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RhythmIsland.Abstractions;
using RhythmIsland.Models;
using RhythmIsland.Theming.Background;

namespace RhythmIsland.ViewModels;

public sealed class RhythmIslandSettingsPageViewModel : ObservableObject
{
    private readonly IRhythmIslandRuntimeService _runtime;
    private string _restartCaptureMessage = "声音异常或状态未同步时，可以重启默认扬声器捕获。";

    public RhythmIslandSettingsPageViewModel(
        RhythmIslandSettings settings,
        RuntimeStatus status,
        IRhythmIslandRuntimeService runtime,
        BackgroundThemeStatus backgroundThemeStatus)
    {
        Settings = settings;
        Status = status;
        BackgroundThemeStatus = backgroundThemeStatus;
        _runtime = runtime;
        RestartCaptureCommand = new AsyncRelayCommand(RestartCaptureAsync);
    }

    public RhythmIslandSettings Settings { get; }
    public RuntimeStatus Status { get; }
    public BackgroundThemeStatus BackgroundThemeStatus { get; }
    public IAsyncRelayCommand RestartCaptureCommand { get; }

    public string RestartCaptureMessage
    {
        get => _restartCaptureMessage;
        private set => SetProperty(ref _restartCaptureMessage, value);
    }

    private async Task RestartCaptureAsync()
    {
        RestartCaptureMessage = "正在重新启动声音捕获…";
        try
        {
            var restarted = await _runtime.RestartCaptureAsync();
            RestartCaptureMessage = restarted
                ? "重启请求已完成，请查看下方当前状态和最近频谱帧。"
                : "当前未启用捕获，或应用正在退出，未执行重启。";
        }
        catch (Exception)
        {
            RestartCaptureMessage = "重启未完成，请查看下方最近错误。";
        }
    }
}
