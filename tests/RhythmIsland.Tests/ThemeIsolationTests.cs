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
}
