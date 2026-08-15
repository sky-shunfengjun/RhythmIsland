using Microsoft.Extensions.Logging.Abstractions;
using RhythmIsland.Models;
using RhythmIsland.Services;

namespace RhythmIsland.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RhythmIsland.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultsMatchPlan()
    {
        var settings = new RhythmIslandSettings();
        Assert.False(settings.IsEnabled);
        Assert.Equal(1.0, settings.Sensitivity);
        Assert.Equal(0.65, settings.Smoothing);
        Assert.Equal(SpectrumVisualizationStyle.Bars, settings.BackgroundSpectrum.VisualizationStyle);
        Assert.Equal(SpectrumDisplayMode.BottomUp, settings.BackgroundSpectrum.DisplayMode);
        Assert.Equal(SpectrumColorSource.ThemeAccent, settings.BackgroundSpectrum.ColorSource);
        Assert.Equal(48, settings.BackgroundSpectrum.BarCount);
        Assert.True(settings.BackgroundSpectrum.HorizontalMirrorEnabled);
        Assert.Equal(SpectrumFrequencyBalanceMode.Balanced, settings.BackgroundSpectrum.FrequencyBalanceMode);
        Assert.Equal(30, settings.BackgroundSpectrum.FrameRate);
        Assert.Equal(0.80, settings.BackgroundSpectrum.Opacity);
        Assert.True(settings.BackgroundSpectrum.IsEnabled);
        Assert.True(settings.BackgroundSpectrum.IsFixedWidthEnabled);
        Assert.Equal(240, settings.BackgroundSpectrum.FixedWidth);
    }

    [Fact]
    public void InvalidValuesAreCorrected()
    {
        var settings = new RhythmIslandSettings
        {
            Sensitivity = double.NaN,
            Smoothing = 4
        };
        Assert.Equal(1, settings.Sensitivity);
        Assert.Equal(1, settings.Smoothing);
    }

    [Fact]
    public void SaveAtomicallyReplacesFileAndCleansTemporaryFile()
    {
        using var store = CreateStore();
        store.Settings.IsEnabled = true;
        store.Settings.Sensitivity = 2.25;
        store.Save();
        Assert.True(File.Exists(store.SettingsPath));
        Assert.False(File.Exists(store.SettingsPath + ".tmp"));

        using var reloaded = CreateStore();
        Assert.True(reloaded.Settings.IsEnabled);
        Assert.Equal(2.25, reloaded.Settings.Sensitivity);
    }

    [Fact]
    public void CorruptJsonFallsBackToDefaultsAndRemovesStaleTemporaryFile()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "settings.json"), "{broken");
        File.WriteAllText(Path.Combine(_folder, "settings.json.tmp"), "stale");
        using var store = CreateStore();
        Assert.False(store.Settings.IsEnabled);
        Assert.False(File.Exists(Path.Combine(_folder, "settings.json.tmp")));
    }

    [Fact]
    public void LegacyGlobalAppearanceFieldsAreIgnored()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "settings.json"),
            """{"IsEnabled":true,"BarCount":96,"SpectrumOpacity":0.2,"CustomColor":"#FFFFFFFF","Sensitivity":1.5}""");

        using var store = CreateStore();
        Assert.True(store.Settings.IsEnabled);
        Assert.Equal(1.5, store.Settings.Sensitivity);
        Assert.Equal(0.80, store.Settings.BackgroundSpectrum.Opacity);
    }

    [Fact]
    public void BackgroundSettingsAreSavedWhenNestedValueChanges()
    {
        using (var store = CreateStore())
        {
            store.Settings.BackgroundSpectrum.BarCount = 96;
            store.Settings.BackgroundSpectrum.Opacity = 0.8;
            store.Settings.BackgroundSpectrum.GlowEnabled = true;
            store.Settings.BackgroundSpectrum.IsEnabled = false;
            store.Settings.BackgroundSpectrum.IsFixedWidthEnabled = false;
            store.Settings.BackgroundSpectrum.FixedWidth = 720;
        }

        using var reloaded = CreateStore();
        Assert.Equal(96, reloaded.Settings.BackgroundSpectrum.BarCount);
        Assert.Equal(0.8, reloaded.Settings.BackgroundSpectrum.Opacity);
        Assert.True(reloaded.Settings.BackgroundSpectrum.GlowEnabled);
        Assert.False(reloaded.Settings.BackgroundSpectrum.IsEnabled);
        Assert.False(reloaded.Settings.BackgroundSpectrum.IsFixedWidthEnabled);
        Assert.Equal(720, reloaded.Settings.BackgroundSpectrum.FixedWidth);
    }

    [Fact]
    public void BackgroundFixedWidthValidationAndSavedOpacityArePreserved()
    {
        var settings = new SpectrumBackgroundSettings { FixedWidth = double.NaN };
        Assert.Equal(240, settings.FixedWidth);
        settings.FixedWidth = 20;
        Assert.Equal(120, settings.FixedWidth);
        settings.FixedWidth = 5000;
        Assert.Equal(1920, settings.FixedWidth);

        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "settings.json"),
            """{"BackgroundSpectrum":{"Opacity":0.35}}""");
        using var store = CreateStore();
        Assert.Equal(0.35, store.Settings.BackgroundSpectrum.Opacity);
        Assert.True(store.Settings.BackgroundSpectrum.IsFixedWidthEnabled);
        Assert.Equal(240, store.Settings.BackgroundSpectrum.FixedWidth);
    }

    [Fact]
    public async Task AutomaticSaveCoalescesRapidChanges()
    {
        var writer = new CountingSettingsFileWriter();
        using var store = new RhythmIslandSettingsStore(
            _folder,
            NullLogger<RhythmIslandSettingsStore>.Instance,
            TimeSpan.FromMilliseconds(60),
            Task.Delay,
            writer);

        for (var index = 0; index < 20; index++)
            store.Settings.BackgroundSpectrum.Opacity = 0.1 + index * 0.04;

        await WaitUntilAsync(() => writer.WriteCount == 1, TimeSpan.FromSeconds(2));
        Assert.Equal(1, writer.WriteCount);
        Assert.False(File.Exists(store.SettingsPath + ".tmp"));
    }

    [Fact]
    public async Task RuntimeOnlyBackgroundPropertiesDoNotScheduleSave()
    {
        var writer = new CountingSettingsFileWriter();
        using var store = new RhythmIslandSettingsStore(
            _folder,
            NullLogger<RhythmIslandSettingsStore>.Instance,
            TimeSpan.FromMilliseconds(40),
            Task.Delay,
            writer);

        store.Settings.BackgroundSpectrum.SetMediaCoverStatusText("运行时状态");
        store.Settings.BackgroundSpectrum.SetAvailableFrameRates(
            [new SpectrumFrameRateOption(30, "30 FPS")]);
        await Task.Delay(120);

        Assert.Equal(0, writer.WriteCount);
    }

    [Fact]
    public void DisposeFlushesTheLastPendingChange()
    {
        var writer = new CountingSettingsFileWriter();
        var store = new RhythmIslandSettingsStore(
            _folder,
            NullLogger<RhythmIslandSettingsStore>.Instance,
            TimeSpan.FromHours(1),
            Task.Delay,
            writer);
        store.Settings.BackgroundSpectrum.Amplitude = 1.75;

        store.Dispose();

        Assert.Equal(1, writer.WriteCount);
        Assert.Contains("\"Amplitude\": 1.75", File.ReadAllText(store.SettingsPath));
    }

    private RhythmIslandSettingsStore CreateStore() => new(_folder, NullLogger<RhythmIslandSettingsStore>.Instance);

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
    }

    private sealed class CountingSettingsFileWriter : ISettingsFileWriter
    {
        private int _writeCount;
        internal int WriteCount => Volatile.Read(ref _writeCount);

        public void Write(string settingsPath, byte[] contents)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllBytes(settingsPath, contents);
            Interlocked.Increment(ref _writeCount);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }
}
