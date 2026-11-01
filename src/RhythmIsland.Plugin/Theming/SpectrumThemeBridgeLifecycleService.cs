using Avalonia;
using Avalonia.Threading;
using ClassIsland.Core;
using Microsoft.Extensions.Hosting;
using RhythmIsland.Theming.Background;

namespace RhythmIsland.Theming;

internal sealed class SpectrumThemeBridgeLifecycleService(
    SpectrumBackgroundHostService hostService,
    BackgroundBridgeStyleController styles) : IHostedService
{
    private AppBase? _application;
    private bool _started;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_started || cancellationToken.IsCancellationRequested) return;
            _started = true;
            SpectrumThemeBridge.Bind(hostService);
            _application = Application.Current as AppBase;
            if (_application is not null)
            {
                _application.AppStarted += OnAppStarted;
                _application.AppStopping += OnAppStopping;
            }
            // 托管服务可能晚于 AppStarted 启动，必须立即应用一次。
            styles.Apply(Application.Current);
        });
    }

    private void OnAppStarted(object? sender, EventArgs eventArgs) => Dispatch(() =>
    {
        if (_started) styles.Apply(Application.Current, reapply: true);
    });

    private void OnAppStopping(object? sender, EventArgs eventArgs) => Dispatch(Stop);

    public async Task StopAsync(CancellationToken cancellationToken) =>
        await Dispatcher.UIThread.InvokeAsync(Stop);

    private void Stop()
    {
        if (!_started) return;
        _started = false;
        if (_application is not null)
        {
            _application.AppStarted -= OnAppStarted;
            _application.AppStopping -= OnAppStopping;
            _application = null;
        }
        styles.Remove();
        SpectrumThemeBridge.Unbind(hostService);
    }

    private static void Dispatch(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
