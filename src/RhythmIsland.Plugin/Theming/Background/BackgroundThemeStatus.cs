using CommunityToolkit.Mvvm.ComponentModel;

namespace RhythmIsland.Theming.Background;

public enum BackgroundThemeState
{
    NotEnabled,
    EnabledWaiting,
    DisabledByUser,
    Displaying,
    Incompatible,
    ContractMismatch
}

public sealed class BackgroundThemeStatus : ObservableObject
{
    private BackgroundThemeState _state = BackgroundThemeState.NotEnabled;
    private string _statusText = "未启用配套背景主题。";

    public BackgroundThemeState State
    {
        get => _state;
        internal set => SetProperty(ref _state, value);
    }

    public string StatusText
    {
        get => _statusText;
        internal set => SetProperty(ref _statusText, value);
    }

    internal void Set(BackgroundThemeState state, string text)
    {
        State = state;
        StatusText = text;
    }
}
