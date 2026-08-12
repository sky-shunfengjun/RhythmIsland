using RhythmIsland.Models;

namespace RhythmIsland.ViewModels;

public sealed class RhythmIslandSettingsPageViewModel
{
    public RhythmIslandSettingsPageViewModel(RhythmIslandSettings settings, RuntimeStatus status)
    {
        Settings = settings;
        Status = status;
    }

    public RhythmIslandSettings Settings { get; }
    public RuntimeStatus Status { get; }
}
