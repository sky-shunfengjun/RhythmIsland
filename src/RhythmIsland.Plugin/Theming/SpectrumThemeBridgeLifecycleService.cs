using Microsoft.Extensions.Hosting;
using RhythmIsland.Theming.Background;

namespace RhythmIsland.Theming;

internal sealed class SpectrumThemeBridgeLifecycleService(SpectrumBackgroundHostService hostService) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        SpectrumThemeBridge.Bind(hostService);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        SpectrumThemeBridge.Unbind(hostService);
        return Task.CompletedTask;
    }
}
