namespace RhythmIsland.Tests;

public sealed class ThemeIsolationTests
{
    [Fact]
    public void PluginSourceDoesNotRegisterThemeAndStandaloneThemeUsesStylesRoot()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var pluginSource = File.ReadAllText(Path.Combine(repositoryRoot, "src", "RhythmIsland.Plugin", "Plugin.cs"));
        var themeSource = File.ReadAllText(Path.Combine(repositoryRoot, "src", "RhythmIsland.Theme", "Styles.axaml"));
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
