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
    }

    private RhythmIslandSettingsStore CreateStore() => new(_folder, NullLogger<RhythmIslandSettingsStore>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }
}
