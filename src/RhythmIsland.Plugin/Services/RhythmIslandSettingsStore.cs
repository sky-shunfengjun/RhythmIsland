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
    private readonly TimeSpan _automaticSaveDelay;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly ISettingsFileWriter _fileWriter;
    private readonly object _saveSync = new();
    private readonly object _scheduleSync = new();
    private SpectrumBackgroundSettings? _subscribedBackgroundSettings;
    private CancellationTokenSource? _scheduledSaveCancellation;
    private Task _scheduledSaveTask = Task.CompletedTask;
    private bool _hasPendingSave;
    private bool _suppressAutomaticSave;
    private bool _disposed;

    public RhythmIslandSettingsStore(string configFolder, ILogger<RhythmIslandSettingsStore> logger)
        : this(configFolder, logger, TimeSpan.FromMilliseconds(400), Task.Delay, new AtomicSettingsFileWriter())
    {
    }

    internal RhythmIslandSettingsStore(
        string configFolder,
        ILogger<RhythmIslandSettingsStore> logger,
        TimeSpan automaticSaveDelay,
        Func<TimeSpan, CancellationToken, Task> delayAsync,
        ISettingsFileWriter fileWriter)
    {
        _logger = logger;
        _settingsPath = Path.Combine(configFolder, "settings.json");
        _automaticSaveDelay = automaticSaveDelay;
        _delayAsync = delayAsync;
        _fileWriter = fileWriter;
        Settings = Load();
        Settings.PropertyChanged += OnSettingsChanged;
        SubscribeBackgroundSettings(Settings.BackgroundSpectrum);
    }

    public RhythmIslandSettings Settings { get; }
    internal string SettingsPath => _settingsPath;

    public void Save()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelPendingSave();
        SaveCore();
    }

    private void SaveCore()
    {
        lock (_saveSync)
        {
            try
            {
                _suppressAutomaticSave = true;
                Settings.Validate();
                var snapshot = JsonSerializer.SerializeToUtf8Bytes(Settings, JsonOptions);
                _fileWriter.Write(_settingsPath, snapshot);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "保存律动岛设置失败。");
                TryDeleteTemporaryFile(_settingsPath + ".tmp");
                throw;
            }
            finally
            {
                _suppressAutomaticSave = false;
            }
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
        if (eventArgs.PropertyName == nameof(RhythmIslandSettings.BackgroundSpectrum))
            SubscribeBackgroundSettings(Settings.BackgroundSpectrum);

        ScheduleSave();
    }

    private void SubscribeBackgroundSettings(SpectrumBackgroundSettings settings)
    {
        if (ReferenceEquals(_subscribedBackgroundSettings, settings)) return;
        if (_subscribedBackgroundSettings is not null)
            _subscribedBackgroundSettings.PropertyChanged -= OnBackgroundSettingsChanged;
        _subscribedBackgroundSettings = settings;
        _subscribedBackgroundSettings.PropertyChanged += OnBackgroundSettingsChanged;
    }

    private void OnBackgroundSettingsChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(SpectrumVisualSettings.MediaCoverStatusText)
            or nameof(SpectrumVisualSettings.AvailableFrameRates)) return;
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        if (_disposed || _suppressAutomaticSave) return;
        CancellationTokenSource cancellation;
        lock (_scheduleSync)
        {
            if (_disposed) return;
            _hasPendingSave = true;
            _scheduledSaveCancellation?.Cancel();
            cancellation = new CancellationTokenSource();
            _scheduledSaveCancellation = cancellation;
            _scheduledSaveTask = SaveAfterDelayAsync(cancellation);
        }
    }

    private async Task SaveAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await _delayAsync(_automaticSaveDelay, cancellation.Token).ConfigureAwait(false);
            lock (_scheduleSync)
            {
                if (_disposed || cancellation.IsCancellationRequested ||
                    !ReferenceEquals(_scheduledSaveCancellation, cancellation)) return;
                _hasPendingSave = false;
            }
            SaveCore();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch
        {
            // SaveCore has already logged the failure. Automatic persistence must not crash the host.
        }
        finally
        {
            lock (_scheduleSync)
            {
                if (ReferenceEquals(_scheduledSaveCancellation, cancellation))
                    _scheduledSaveCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void CancelPendingSave()
    {
        lock (_scheduleSync)
        {
            _hasPendingSave = false;
            _scheduledSaveCancellation?.Cancel();
            _scheduledSaveCancellation = null;
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Settings.PropertyChanged -= OnSettingsChanged;
        if (_subscribedBackgroundSettings is not null)
            _subscribedBackgroundSettings.PropertyChanged -= OnBackgroundSettingsChanged;

        Task scheduledTask;
        bool mustFlush;
        lock (_scheduleSync)
        {
            _disposed = true;
            mustFlush = _hasPendingSave;
            _hasPendingSave = false;
            _scheduledSaveCancellation?.Cancel();
            _scheduledSaveCancellation = null;
            scheduledTask = _scheduledSaveTask;
        }

        try { scheduledTask.GetAwaiter().GetResult(); }
        catch { /* The delayed task logs write errors and treats cancellation as normal. */ }
        if (!mustFlush) return;
        try { SaveCore(); }
        catch { /* Final persistence failure is logged but must not crash ClassIsland shutdown. */ }
    }
}

internal interface ISettingsFileWriter
{
    void Write(string settingsPath, byte[] contents);
}

internal sealed class AtomicSettingsFileWriter : ISettingsFileWriter
{
    public void Write(string settingsPath, byte[] contents)
    {
        var directory = Path.GetDirectoryName(settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = settingsPath + ".tmp";
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                   4096, FileOptions.WriteThrough))
        {
            stream.Write(contents);
            stream.Flush(true);
        }

        File.Move(temporaryPath, settingsPath, true);
    }
}
