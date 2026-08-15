using System.Xml.Linq;

namespace RhythmIsland.Tests;

public sealed class SpectrumSettingsLayoutTests
{
    [Fact]
    public void SettingsCardsFollowAppearanceSpectrumAndFunctionGroups()
    {
        var appearance = LoadAppearanceSettings();
        var frequency = LoadFrequencySettings();
        var component = LoadComponentSettings();
        var sequence = new[]
        {
            "外观"
        }.Concat(ExpanderHeaders(appearance))
            .Concat(["基础外观", "频谱"])
            .Concat(ExpanderHeaders(frequency))
            .Concat(["功能", "无声自动收起"])
            .ToArray();

        Assert.Equal(
        [
            "外观", "显示样式", "频谱方向", "颜色与渐变", "发光效果", "基础外观",
            "频谱", "细节数量", "中心镜像", "频率均衡补偿", "幅度", "刷新率",
            "功能", "无声自动收起"
        ], sequence);
    }

    [Fact]
    public void SpectrumDisplayOptionsUseRequestedLabelsAndOrder()
    {
        var root = LoadFrequencySettings();
        var expanders = root.Descendants().Where(element => element.Name.LocalName == "SettingsExpander").ToArray();
        var mirror = expanders.Single(element => (string?)element.Attribute("Header") == "中心镜像");
        var balance = expanders.Single(element => (string?)element.Attribute("Header") == "频率均衡补偿");

        Assert.Contains("低频放在中间", (string?)mirror.Attribute("Description"));
        Assert.Equal(
            ["原始", "均衡", "突出高频"],
            balance.Descendants()
                .Where(element => element.Name.LocalName == "ComboBoxItem")
                .Select(element => (string)element.Attribute("Content")!)
                .ToArray());
    }

    [Fact]
    public void ColorAndGradientItemsUseRequestedOrderAndBasicAppearanceStartsExpanded()
    {
        var appearance = LoadAppearanceSettings();
        var component = LoadComponentSettings();
        var color = appearance.Descendants().Single(element => element.Name.LocalName == "SettingsExpander" && (string?)element.Attribute("Header") == "颜色与渐变");
        var basic = component.Descendants().Single(element => element.Name.LocalName == "SettingsExpander" && (string?)element.Attribute("Header") == "基础外观");

        var colorItems = color.Descendants()
            .Where(element => element.Name.LocalName == "SettingsExpanderItem")
            .Select(element => (string)element.Attribute("Content")!)
            .ToArray();

        Assert.Equal(["颜色来源", "主色", "第二色", "封面状态", "颜色模式", "渐变方式", "流动速度"], colorItems);
        Assert.Equal("True", (string?)basic.Attribute("IsExpanded"));
        Assert.Equal(
            ["不透明度", "组件长度"],
            basic.Descendants()
                .Where(element => element.Name.LocalName == "SettingsExpanderItem")
                .Select(element => (string)element.Attribute("Content")!)
                .ToArray());
    }

    [Fact]
    public void MediaCoverOptionClearlyMentionsSmtc()
    {
        var root = LoadAppearanceSettings();
        Assert.Contains(root.Descendants(), element =>
            element.Name.LocalName == "ComboBoxItem" &&
            (string?)element.Attribute("Content") == "音乐封面（SMTC）");
    }

    [Fact]
    public void MediaColorModeAppearsAfterCoverStatusAndOnlyForMediaCover()
    {
        var root = LoadAppearanceSettings();
        var item = root.Descendants().Single(element =>
            element.Name.LocalName == "SettingsExpanderItem" &&
            (string?)element.Attribute("Content") == "颜色模式");

        Assert.Equal("{Binding Settings.IsMediaCoverColorSource}", (string?)item.Attribute("IsVisible"));
        Assert.Equal(
            ["鲜艳", "柔和", "偏色"],
            item.Descendants()
                .Where(element => element.Name.LocalName == "ComboBoxItem")
                .Select(element => (string)element.Attribute("Content")!)
                .ToArray());
    }

    [Fact]
    public void BackgroundBasicAppearanceUsesOpacityFixedWidthAndLengthOrder()
    {
        var root = LoadSettingsFile("Views", "SettingsPages", "RhythmIslandSettingsPage.axaml");
        var basic = root.Descendants().Single(element =>
            element.Name.LocalName == "SettingsExpander" &&
            (string?)element.Attribute("Header") == "基础外观");

        Assert.Equal(
            ["不透明度", "固定长度", "频谱长度"],
            basic.Descendants()
                .Where(element => element.Name.LocalName == "SettingsExpanderItem")
                .Select(element => (string)element.Attribute("Content")!)
                .ToArray());

        var fixedWidthToggle = basic.Descendants().Single(element => element.Name.LocalName == "ToggleSwitch");
        var lengthSlider = basic.Descendants().Single(element =>
            element.Name.LocalName == "Slider" &&
            ((string?)element.Attribute("Value"))?.Contains("FixedWidth", StringComparison.Ordinal) == true);
        Assert.Contains("IsFixedWidthEnabled", (string?)fixedWidthToggle.Attribute("IsChecked"));
        Assert.Equal("120", (string?)lengthSlider.Attribute("Minimum"));
        Assert.Equal("1920", (string?)lengthSlider.Attribute("Maximum"));
        Assert.Equal("10", (string?)lengthSlider.Attribute("TickFrequency"));
    }

    [Fact]
    public void FluentIconsUseTheCurrentApprovedMapping()
    {
        var plugin = LoadSettingsFile("Views", "SettingsPages", "RhythmIslandSettingsPage.axaml");
        var appearance = LoadAppearanceSettings();
        var frequency = LoadFrequencySettings();
        var component = LoadComponentSettings();

        AssertExpanderIcon(plugin, "运行状态", '\uEDB9');
        AssertExpanderIcon(plugin, "重启捕获", '\uE0B5');
        AssertExpanderIcon(plugin, "捕获诊断", '\uE9D9');
        AssertExpanderIcon(plugin, "隐私说明", '\uE946');
        AssertExpanderIcon(plugin, "背景频谱", '\uEFF7');
        AssertExpanderIcon(plugin, "主题状态", '\uE82F');
        AssertExpanderIcon(plugin, "基础外观", '\uEE83');

        AssertExpanderIcon(appearance, "显示样式", '\uEAAA');
        AssertExpanderIcon(appearance, "频谱方向", '\uE09B');
        AssertExpanderIcon(appearance, "颜色与渐变", '\uE51E');
        AssertExpanderIcon(appearance, "发光效果", '\uE85F');

        AssertExpanderIcon(frequency, "细节数量", '\uE5D1');
        AssertExpanderIcon(frequency, "中心镜像", '\uE09B');
        AssertExpanderIcon(frequency, "频率均衡补偿", '\uE9D9');
        AssertExpanderIcon(frequency, "幅度", '\uEFF7');
        AssertExpanderIcon(frequency, "刷新率", '\uE8E1');
        AssertExpanderIcon(component, "无声自动收起", '\uE817');

        Assert.Contains("\\ueff7", File.ReadAllText(RepositoryPath(
            "src", "RhythmIsland.Plugin", "Views", "SettingsPages", "RhythmIslandSettingsPage.axaml.cs")),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\\ueff7", File.ReadAllText(RepositoryPath(
            "src", "RhythmIsland.Plugin", "Controls", "Components", "SpectrumComponent.axaml.cs")),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertExpanderIcon(XElement root, string header, char glyph)
    {
        var expander = root.Descendants().Single(element =>
            element.Name.LocalName == "SettingsExpander" &&
            (string?)element.Attribute("Header") == header);
        Assert.Contains(glyph, (string?)expander.Attribute("IconSource") ?? string.Empty);
    }

    private static string[] ExpanderHeaders(XElement root) => root.Descendants()
        .Where(element => element.Name.LocalName == "SettingsExpander")
        .Select(element => (string?)element.Attribute("Header"))
        .Where(value => value is not null)
        .Select(value => value!)
        .ToArray();

    private static XElement LoadComponentSettings() => LoadSettingsFile(
        "Controls", "Components", "SpectrumComponentSettingsControl.axaml");

    private static XElement LoadAppearanceSettings() => LoadSettingsFile(
        "Controls", "Settings", "SpectrumAppearanceSettingsControl.axaml");

    private static XElement LoadFrequencySettings() => LoadSettingsFile(
        "Controls", "Settings", "SpectrumFrequencySettingsControl.axaml");

    private static XElement LoadSettingsFile(params string[] relativeParts)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine([repositoryRoot, "src", "RhythmIsland.Plugin", .. relativeParts]);
        return XElement.Load(path);
    }

    private static string RepositoryPath(params string[] parts)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        return Path.Combine([repositoryRoot, .. parts]);
    }
}
