using RhythmIsland.Models;
using RhythmIsland.Services;

namespace RhythmIsland.Tests;

public sealed class SpectrumDisplayCapabilityTests
{
    [Fact]
    public void RegistryUsesTheActualVisualCapabilityAndClearsItWithTheLease()
    {
        var service = new SpectrumDisplayCapabilityService();
        var settings = new SpectrumComponentSettings { FrameRate = 120 };
        var refreshRate = 75d;
        using var lease = service.Register(settings, () => refreshRate);

        Assert.Equal(75, service.GetRefreshRate(settings));
        Assert.Equal(60, SpectrumFrameRatePolicy.ResolvePersistedFrameRate(
            settings.FrameRate, service.GetRefreshRate(settings)));

        refreshRate = 144;
        service.Refresh(settings);
        Assert.Equal(144, service.GetRefreshRate(settings));

        lease.Dispose();
        Assert.Null(service.GetRefreshRate(settings));
    }

    [Fact]
    public void RegistryKeepsComponentAndBackgroundCapabilitiesIndependent()
    {
        var service = new SpectrumDisplayCapabilityService();
        var component = new SpectrumComponentSettings { FrameRate = 90 };
        var background = new SpectrumBackgroundSettings { FrameRate = 120 };
        using var componentLease = service.Register(component, () => 90);
        using var backgroundLease = service.Register(background, () => 144);

        Assert.Equal(90, service.GetRefreshRate(component));
        Assert.Equal(144, service.GetRefreshRate(background));
        Assert.Equal([30, 60, 90, 0],
            SpectrumFrameRateOptions.ForRefreshRate(service.GetRefreshRate(component)).Select(option => option.Value));
        Assert.Equal([30, 60, 90, 120, 0],
            SpectrumFrameRateOptions.ForRefreshRate(service.GetRefreshRate(background)).Select(option => option.Value));
    }

    [Fact]
    public void UnknownCapabilityPreservesSavedHighFrameRate()
    {
        var service = new SpectrumDisplayCapabilityService();
        var settings = new SpectrumBackgroundSettings { FrameRate = 120 };
        using var lease = service.Register(settings, () => null);

        var detected = service.GetRefreshRate(settings);

        Assert.Null(detected);
        Assert.Equal(120, SpectrumFrameRatePolicy.ResolvePersistedFrameRate(settings.FrameRate, detected));
        Assert.Equal(60, SpectrumFrameRatePolicy.ResolveEffectiveFrameRate(settings.FrameRate, detected));
    }
}
