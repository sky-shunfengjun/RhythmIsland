using System.Reflection;
using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace RhythmIsland.Theming.Background;

internal sealed class BackgroundBridgeStyleController(
    SpectrumBackgroundHostService hostService,
    ILogger<BackgroundBridgeStyleController> logger)
{
    private Application? _application;
    private Styles? _styles;

    public void Apply(Application? application, bool reapply = false)
    {
        Dispatcher.UIThread.VerifyAccess();
        try
        {
            if (application is null) throw new InvalidOperationException("ClassIsland 应用实例尚未就绪。");
            if (_application is not null && !ReferenceEquals(_application, application)) Remove();
            _styles ??= LoadStyles(application.GetType().Assembly);
            _application = application;
            if (reapply) application.Styles.Remove(_styles);
            if (!application.Styles.Contains(_styles)) application.Styles.Add(_styles);
            hostService.SetBridgeStyleState(applied: true);
        }
        catch (Exception exception)
        {
            hostService.SetBridgeStyleState(applied: false, failed: true);
            logger.LogError(exception, "律动频谱内置背景样式加载失败，普通频谱组件仍可使用。");
        }
    }

    public void Remove()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_styles is not null) _application?.Styles.Remove(_styles);
        _styles = null;
        _application = null;
        hostService.SetBridgeStyleState(applied: false);
    }

    private static Styles LoadStyles(Assembly hostAssembly)
    {
        using var stream = typeof(BackgroundBridgeStyleController).Assembly.GetManifestResourceStream(
            "RhythmIsland.Theming.Background.BackgroundBridgeStyles.axaml")
            ?? throw new InvalidOperationException("插件安装包缺少内置背景样式。");
        using var reader = new StreamReader(stream);
        // MainWindowLine 只存在于宿主中，不可作为插件编译期类型引用。
        return AvaloniaRuntimeXamlLoader.Load(reader.ReadToEnd(), hostAssembly) as Styles
            ?? throw new InvalidOperationException("内置背景样式的根对象不是 Styles。");
    }
}
