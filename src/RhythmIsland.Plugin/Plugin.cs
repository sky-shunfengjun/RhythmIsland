using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RhythmIsland.Abstractions;
using RhythmIsland.Controls.Components;
using RhythmIsland.Models;
using RhythmIsland.Services;
using RhythmIsland.Views.SettingsPages;

namespace RhythmIsland;

[PluginEntrance]
public sealed class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        services.AddSingleton(serviceProvider => new RhythmIslandSettingsStore(
            PluginConfigFolder,
            serviceProvider.GetRequiredService<ILogger<RhythmIslandSettingsStore>>()));
        services.AddSingleton<RuntimeStatus>();
        services.AddSingleton<IAudioCaptureService, WindowsAudioCaptureService>();
        services.AddSingleton<ISpectrumAnalyzer, SpectrumAnalyzer>();
        services.AddSingleton<ISpectrumFrameProvider, SpectrumFrameProvider>();
        services.AddSingleton<ISpectrumRenderClock, SpectrumRenderClock>();
        services.AddSingleton<ISystemMediaCoverService, SystemMediaCoverService>();
        services.AddSingleton<RhythmIslandRuntimeService>();
        services.AddSingleton<IRhythmIslandRuntimeService>(provider => provider.GetRequiredService<RhythmIslandRuntimeService>());
        services.AddHostedService(provider => provider.GetRequiredService<RhythmIslandRuntimeService>());
        services.AddComponent<SpectrumComponent, SpectrumComponentSettingsControl>();
        services.AddSettingsPage<RhythmIslandSettingsPage>();
    }
}
