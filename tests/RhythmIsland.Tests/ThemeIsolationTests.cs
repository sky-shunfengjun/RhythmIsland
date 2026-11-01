namespace RhythmIsland.Tests;

public sealed class ThemeIsolationTests
{
    [Fact]
    public void BuiltPluginReadmeMatchesRepositoryReadme()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var packagedReadme = Path.Combine(repositoryRoot, "src", "RhythmIsland.Plugin", "bin", configuration,
            "net8.0-windows10.0.19041.0", "README.md");
        Assert.Equal(File.ReadAllText(Path.Combine(repositoryRoot, "README.md")), File.ReadAllText(packagedReadme));
    }

    [Fact]
    public void PluginSourceDoesNotRegisterThemeAndBuiltinBridgeUsesStylesRoot()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var pluginSource = File.ReadAllText(Path.Combine(repositoryRoot, "src", "RhythmIsland.Plugin", "Plugin.cs"));
        var stylePath = Path.Combine(repositoryRoot, "src", "RhythmIsland.Plugin", "Theming", "Background", "BackgroundBridgeStyles.axaml");
        Assert.True(File.Exists(stylePath), "背景样式必须随插件维护。");
        var themeSource = File.ReadAllText(stylePath);
        Assert.DoesNotContain("AddXamlTheme", pluginSource, StringComparison.Ordinal);
        Assert.StartsWith("<Styles", themeSource.TrimStart(), StringComparison.Ordinal);
        Assert.DoesNotContain("ResourceDictionary", themeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PluginDoesNotReferenceOrPackageMediaIsland()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var project = File.ReadAllText(Path.Combine(repositoryRoot, "src", "RhythmIsland.Plugin", "RhythmIsland.csproj"));
        var source = string.Join('\n', Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, "src", "RhythmIsland.Plugin"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText));

        Assert.DoesNotContain("MediaIsland.dll", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MediaIsland.Services", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BuiltPluginShipsRuntimeLoaderWithoutOtherAvaloniaAssemblies()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var output = Path.Combine(repositoryRoot, "src", "RhythmIsland.Plugin", "bin", configuration,
            "net8.0-windows10.0.19041.0");
        var assemblies = Directory.GetFiles(output, "Avalonia*.dll", SearchOption.AllDirectories);
        Assert.Equal("Avalonia.Markup.Xaml.Loader.dll", Path.GetFileName(Assert.Single(assemblies)));
        Assert.Equal(new Version(11, 3, 17, 0), System.Reflection.AssemblyName.GetAssemblyName(assemblies[0]).Version);
    }

    [Fact]
    public void GlowRenderingDoesNotMutateVisualEffectDuringRender()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var controlSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "RhythmIsland.Plugin",
            "Controls",
            "Components",
            "SpectrumBarsControl.cs"));

        Assert.DoesNotContain("Effect =", controlSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DropShadowEffect", controlSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DrawEllipse", controlSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BodyWidthRatio", controlSource, StringComparison.Ordinal);
    }
}
