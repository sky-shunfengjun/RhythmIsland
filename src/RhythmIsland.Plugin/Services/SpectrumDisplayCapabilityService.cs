using System.Runtime.CompilerServices;
using Avalonia.Controls;
using RhythmIsland.Models;

namespace RhythmIsland.Services;

/// <summary>
/// Records the display capability of the visual that actually renders a spectrum.
/// Settings controls use this registry instead of the screen containing the settings window.
/// </summary>
internal sealed class SpectrumDisplayCapabilityService
{
    private readonly ConditionalWeakTable<SpectrumVisualSettings, SettingsEntry> _entries = new();

    internal event EventHandler<SpectrumDisplayCapabilityChangedEventArgs>? Changed;

    internal SpectrumDisplayCapabilityLease Register(SpectrumVisualSettings settings, Control owner)
    {
        var weakOwner = new WeakReference<Control>(owner);
        return Register(settings, () => ResolveRefreshRate(weakOwner));
    }

    internal SpectrumDisplayCapabilityLease Register(
        SpectrumVisualSettings settings,
        Func<double?> refreshRateProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(refreshRateProvider);

        var entry = _entries.GetOrCreateValue(settings);
        var registration = new Registration(refreshRateProvider);
        lock (entry.SyncRoot)
        {
            entry.Registrations.Add(registration);
        }

        RaiseChanged(settings);
        return new SpectrumDisplayCapabilityLease(this, settings, entry, registration);
    }

    internal double? GetRefreshRate(SpectrumVisualSettings settings)
    {
        if (!_entries.TryGetValue(settings, out var entry)) return null;

        Registration[] registrations;
        lock (entry.SyncRoot)
        {
            registrations = entry.Registrations.Where(item => !item.Disposed).ToArray();
        }

        // Prefer the most recently attached visual. During a template rebuild the old and
        // new visual can coexist briefly; the latest one represents what the user sees.
        for (var index = registrations.Length - 1; index >= 0; index--)
        {
            var refreshRate = ResolveRegistration(settings, registrations[index]);
            if (SpectrumFrameRatePolicy.IsReliable(refreshRate)) return refreshRate;
        }

        return null;
    }

    internal void Refresh(SpectrumVisualSettings settings)
    {
        if (!_entries.TryGetValue(settings, out var entry)) return;
        lock (entry.SyncRoot)
        {
            foreach (var registration in entry.Registrations)
                registration.CacheExpiresAt = 0;
        }
        RaiseChanged(settings);
    }

    private static double? ResolveRefreshRate(WeakReference<Control> weakOwner)
    {
        if (!weakOwner.TryGetTarget(out var owner)) return null;
        try
        {
            var screen = TopLevel.GetTopLevel(owner)?.Screens?.ScreenFromVisual(owner);
            return screen is null ? null : DisplayRefreshRateProvider.GetForBounds(screen.Bounds);
        }
        catch
        {
            return null;
        }
    }

    private double? ResolveRegistration(SpectrumVisualSettings settings, Registration registration)
    {
        var now = Environment.TickCount64;
        if (now < registration.CacheExpiresAt) return registration.CachedRefreshRate;

        registration.CacheExpiresAt = now + 1000;
        try
        {
            var previous = registration.CachedRefreshRate;
            registration.CachedRefreshRate = registration.RefreshRateProvider();
            if (registration.HasResolved && !AreEquivalent(previous, registration.CachedRefreshRate))
                RaiseChanged(settings);
            registration.HasResolved = true;
        }
        catch
        {
            registration.CachedRefreshRate = null;
        }

        return registration.CachedRefreshRate;
    }

    private static bool AreEquivalent(double? left, double? right) =>
        left is null && right is null ||
        left is { } leftValue && right is { } rightValue && Math.Abs(leftValue - rightValue) < 0.1;

    private void Unregister(
        SpectrumVisualSettings settings,
        SettingsEntry entry,
        Registration registration)
    {
        lock (entry.SyncRoot)
        {
            registration.Disposed = true;
            entry.Registrations.Remove(registration);
            if (entry.Registrations.Count == 0) _entries.Remove(settings);
        }
        RaiseChanged(settings);
    }

    private void RaiseChanged(SpectrumVisualSettings settings) =>
        Changed?.Invoke(this, new SpectrumDisplayCapabilityChangedEventArgs(settings));

    internal sealed class SettingsEntry
    {
        internal object SyncRoot { get; } = new();
        internal List<Registration> Registrations { get; } = [];
    }

    internal sealed class Registration(Func<double?> refreshRateProvider)
    {
        internal Func<double?> RefreshRateProvider { get; } = refreshRateProvider;
        internal double? CachedRefreshRate { get; set; }
        internal long CacheExpiresAt { get; set; }
        internal bool Disposed { get; set; }
        internal bool HasResolved { get; set; }
    }

    internal sealed class SpectrumDisplayCapabilityLease : IDisposable
    {
        private SpectrumDisplayCapabilityService? _service;
        private readonly SpectrumVisualSettings _settings;
        private readonly SettingsEntry _entry;
        private readonly Registration _registration;

        internal SpectrumDisplayCapabilityLease(
            SpectrumDisplayCapabilityService service,
            SpectrumVisualSettings settings,
            SettingsEntry entry,
            Registration registration)
        {
            _service = service;
            _settings = settings;
            _entry = entry;
            _registration = registration;
        }

        internal double? GetRefreshRate() =>
            _service is null ? null : _service.ResolveRegistration(_settings, _registration);

        public void Dispose()
        {
            var service = Interlocked.Exchange(ref _service, null);
            service?.Unregister(_settings, _entry, _registration);
        }
    }
}

internal sealed class SpectrumDisplayCapabilityChangedEventArgs(SpectrumVisualSettings settings) : EventArgs
{
    internal SpectrumVisualSettings Settings { get; } = settings;
}
