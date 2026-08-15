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
using RhythmIsland.Theming;
using RhythmIsland.Theming.Background;
using RhythmIsland.Views.SettingsPages;

namespace RhythmIsland;

[PluginEntrance]
public sealed class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // 外部主题加载前注册 Avalonia 自带 Tag 的桥接监听；主题无需解析插件程序集类型。
        SpectrumThemeBridge.Register();
        services.AddSingleton(serviceProvider => new RhythmIslandSettingsStore(
            PluginConfigFolder,
            serviceProvider.GetRequiredService<ILogger<RhythmIslandSettingsStore>>()));
        services.AddSingleton<RuntimeStatus>();
        services.AddSingleton<IAudioCaptureService, WindowsAudioCaptureService>();
        services.AddSingleton<ISpectrumAnalyzer, SpectrumAnalyzer>();
        services.AddSingleton<ISpectrumFrameProvider, SpectrumFrameProvider>();
        services.AddSingleton<ISpectrumRenderClock, SpectrumRenderClock>();
        services.AddSingleton<ISystemMediaCoverService, SystemMediaCoverService>();
        services.AddSingleton<SpectrumDisplayCapabilityService>();
        services.AddSingleton<BackgroundThemeStatus>();
        services.AddSingleton<SpectrumBackgroundHostService>();
        services.AddHostedService<SpectrumThemeBridgeLifecycleService>();
        services.AddSingleton<RhythmIslandRuntimeService>();
        services.AddSingleton<IRhythmIslandRuntimeService>(provider => provider.GetRequiredService<RhythmIslandRuntimeService>());
        services.AddHostedService(provider => provider.GetRequiredService<RhythmIslandRuntimeService>());
        services.AddComponent<SpectrumComponent, SpectrumComponentSettingsControl>();
        services.AddSettingsPage<RhythmIslandSettingsPage>();
    }
}
