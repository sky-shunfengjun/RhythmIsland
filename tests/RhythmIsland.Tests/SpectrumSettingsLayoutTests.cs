using System.Xml.Linq;

namespace RhythmIsland.Tests;

public sealed class SpectrumSettingsLayoutTests
{
    [Fact]
    public void SettingsCardsFollowAppearanceSpectrumAndFunctionGroups()
    {
        var root = LoadSettingsPage();
        var stack = root.Descendants().First(element =>
            element.Name.LocalName == "StackPanel" &&
            ((string?)element.Attribute("Classes"))?.Contains("component-settings-container") == true);

        var sequence = stack.Elements()
            .Select(element => element.Name.LocalName == "IconText"
                ? (string?)element.Attribute("Text")
                : element.Name.LocalName == "SettingsExpander"
                    ? (string?)element.Attribute("Header")
                    : null)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();

        Assert.Equal(
        [
            "外观", "显示样式", "频谱方向", "颜色与渐变", "发光效果", "基础外观",
            "频谱", "细节数量", "幅度", "刷新帧率",
            "功能", "无声自动收起"
        ], sequence);
    }

    [Fact]
    public void ColorAndGradientItemsUseRequestedOrderAndBasicAppearanceStartsExpanded()
    {
        var root = LoadSettingsPage();
        var expanders = root.Descendants().Where(element => element.Name.LocalName == "SettingsExpander").ToArray();
        var color = expanders.Single(element => (string?)element.Attribute("Header") == "颜色与渐变");
        var basic = expanders.Single(element => (string?)element.Attribute("Header") == "基础外观");

        var colorItems = color.Descendants()
            .Where(element => element.Name.LocalName == "SettingsExpanderItem")
            .Select(element => (string)element.Attribute("Content")!)
            .ToArray();

        Assert.Equal(["颜色来源", "主色", "第二色", "封面状态", "颜色模式", "渐变方式", "流动速度"], colorItems);
        Assert.Equal("True", (string?)basic.Attribute("IsExpanded"));
        Assert.Equal(
            ["透明度", "组件长度"],
            basic.Descendants()
                .Where(element => element.Name.LocalName == "SettingsExpanderItem")
                .Select(element => (string)element.Attribute("Content")!)
                .ToArray());
    }

    [Fact]
    public void MediaCoverOptionClearlyMentionsSmtc()
    {
        var root = LoadSettingsPage();
        Assert.Contains(root.Descendants(), element =>
            element.Name.LocalName == "ComboBoxItem" &&
            (string?)element.Attribute("Content") == "音乐封面（SMTC）");
    }

    [Fact]
    public void MediaColorModeAppearsAfterCoverStatusAndOnlyForMediaCover()
    {
        var root = LoadSettingsPage();
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

    private static XElement LoadSettingsPage()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(repositoryRoot, "src", "RhythmIsland.Plugin", "Controls", "Components",
            "SpectrumComponentSettingsControl.axaml");
        return XElement.Load(path);
    }
}
