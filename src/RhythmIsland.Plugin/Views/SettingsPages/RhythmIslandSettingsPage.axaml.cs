using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using RhythmIsland.Models;
using RhythmIsland.Abstractions;
using RhythmIsland.Services;
using RhythmIsland.ViewModels;

namespace RhythmIsland.Views.SettingsPages;

[SettingsPageInfo("rhythmisland.settings", "律动岛", "\ue768", "\ue768")]
public partial class RhythmIslandSettingsPage : SettingsPageBase
{
    public RhythmIslandSettingsPage()
    {
        InitializeComponent();
        var store = IAppHost.GetService<RhythmIslandSettingsStore>();
        var status = IAppHost.GetService<RuntimeStatus>();
        var runtime = IAppHost.GetService<IRhythmIslandRuntimeService>();
        DataContext = new RhythmIslandSettingsPageViewModel(store.Settings, status, runtime);
    }
}
