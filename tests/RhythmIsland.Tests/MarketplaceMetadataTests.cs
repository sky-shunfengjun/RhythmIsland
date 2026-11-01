using YamlDotNet.Serialization;

namespace RhythmIsland.Tests;

public sealed class MarketplaceMetadataTests
{
    [Fact]
    public void MarketMetadataIncludesPluginIdentityCompatibilityAndAssets()
    {
        var plugin = ReadYaml("src", "RhythmIsland.Plugin", "manifest.yml");
        var market = ReadYaml("index", "plugins-v2", "RhythmIsland.yml");
        foreach (var key in plugin.Keys.Where(key => key is not "readme" and not "supportedOSPlatforms"))
        {
            Assert.True(market.ContainsKey(key), $"市场索引缺少 {key}。");
            Assert.Equal(plugin[key], market[key]);
        }
        Assert.True(market.ContainsKey("supportedOSPlatforms"));
        Assert.Equal(new[] { "Windows" },
            Assert.IsAssignableFrom<IEnumerable<object>>(market["supportedOSPlatforms"]).Select(value => value.ToString()));
        Assert.Equal("README.md", plugin["readme"]);
    }

    [Fact]
    public void MarketReadmeResolvesToRepositoryRootWhileIconKeepsPluginAssetLocation()
    {
        var market = ReadYaml("index", "plugins-v2", "RhythmIsland.yml");
        Assert.True(market.ContainsKey("readme"), "市场索引必须显式指定根 README 入口。");
        Assert.True(market.ContainsKey("icon"));
        // Matches PluginIndexGenerator 1.1.1.0, used by the official index workflow:
        // https://github.com/ClassIsland/ClassIsland.PluginIndexGenerator/blob/1.1.1.0/ClassIsland.PluginIndexGenerator/Generators/PluginIndexGenerator.cs
        var assetRoot = $"https://github.com/{market["repoOwner"]}/{market["repoName"]}/raw/{market["assetsRoot"]}/";
        Assert.Equal("https://github.com/sky-shunfengjun/RhythmIsland/raw/main/README.md",
            new Uri(assetRoot + market["readme"]).AbsoluteUri);
        Assert.Equal("https://github.com/sky-shunfengjun/RhythmIsland/raw/main/src/RhythmIsland.Plugin/icon.png",
            new Uri(assetRoot + market["icon"]).AbsoluteUri);
        var repositoryAssetRoot = string.Join('/', market["assetsRoot"].ToString()!.Split('/').Skip(1));
        Assert.Equal(Path.GetFullPath(RepositoryPath("README.md")),
            Path.GetFullPath(Path.Combine(RepositoryPath(repositoryAssetRoot), market["readme"].ToString()!)));
        Assert.True(File.Exists(RepositoryPath(repositoryAssetRoot, market["icon"].ToString()!)));
    }

    private static Dictionary<string, object> ReadYaml(params string[] parts) =>
        new DeserializerBuilder().Build().Deserialize<Dictionary<string, object>>(File.ReadAllText(RepositoryPath(parts)));

    private static string RepositoryPath(params string[] parts) =>
        Path.Combine([Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")), .. parts]);
}
