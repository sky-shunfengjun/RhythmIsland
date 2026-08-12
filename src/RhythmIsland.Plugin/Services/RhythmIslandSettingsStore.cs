using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RhythmIsland.Models;

namespace RhythmIsland.Services;

public sealed class RhythmIslandSettingsStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly ILogger<RhythmIslandSettingsStore> _logger;
    private readonly string _settingsPath;
    private bool _disposed;

    public RhythmIslandSettingsStore(string configFolder, ILogger<RhythmIslandSettingsStore> logger)
    {
        _logger = logger;
        _settingsPath = Path.Combine(configFolder, "settings.json");
        Settings = Load();
        Settings.PropertyChanged += OnSettingsChanged;
    }

    public RhythmIslandSettings Settings { get; }
    internal string SettingsPath => _settingsPath;

    public void Save()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Settings.Validate();
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _settingsPath + ".tmp";

        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, Settings, JsonOptions);
                stream.Flush(true);
            }

            File.Move(temporaryPath, _settingsPath, true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "保存律动岛设置失败。");
            TryDeleteTemporaryFile(temporaryPath);
            throw;
        }
    }

    private RhythmIslandSettings Load()
    {
        if (!File.Exists(_settingsPath)) return new RhythmIslandSettings();

        try
        {
            using var stream = File.OpenRead(_settingsPath);
            var loaded = JsonSerializer.Deserialize<RhythmIslandSettings>(stream, JsonOptions) ?? new RhythmIslandSettings();
            loaded.Validate();
            TryDeleteTemporaryFile(_settingsPath + ".tmp");
            return loaded;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "律动岛设置已损坏或无法读取，将使用默认设置。");
            TryDeleteTemporaryFile(_settingsPath + ".tmp");
            return new RhythmIslandSettings();
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        try { Save(); }
        catch { /* Save already logged; the running plugin keeps its validated in-memory settings. */ }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Settings.PropertyChanged -= OnSettingsChanged;
    }
}
