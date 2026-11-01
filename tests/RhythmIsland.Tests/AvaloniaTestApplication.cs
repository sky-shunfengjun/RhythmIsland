using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(RhythmIsland.Tests.AvaloniaTestApplication))]

namespace RhythmIsland.Tests;

public static class AvaloniaTestApplication
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<RhythmIslandTestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

// 与真实宿主一样，由应用所在程序集提供 MainWindowLine 类型供运行时样式解析。
public sealed class RhythmIslandTestApplication : Application;
