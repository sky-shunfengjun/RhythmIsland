using System.ComponentModel;
using System.Threading.Channels;
using Avalonia.Threading;
using ClassIsland.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RhythmIsland.Abstractions;
using RhythmIsland.Models;

namespace RhythmIsland.Services;

public sealed class RhythmIslandRuntimeService : IHostedService, IRhythmIslandRuntimeService, IDisposable
{
    private sealed record AudioChunk(float[] Samples, int SampleRate);
    private readonly IAudioCaptureService _capture;
    private readonly ISpectrumAnalyzer _analyzer;
    private readonly ISpectrumFrameProvider _frames;
    private readonly RhythmIslandSettingsStore _settingsStore;
    private readonly RuntimeStatus _status;
    private readonly ILogger<RhythmIslandRuntimeService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Channel<AudioChunk>? _channel;
    private CancellationTokenSource? _analysisCancellation;
    private Task? _analysisTask;
    private bool _appStarted;
    private bool _subscribed;
    private bool _disposed;

    public RhythmIslandRuntimeService(IAudioCaptureService capture, ISpectrumAnalyzer analyzer,
        ISpectrumFrameProvider frames, RhythmIslandSettingsStore settingsStore, RuntimeStatus status,
        ILogger<RhythmIslandRuntimeService> logger)
    {
        _capture = capture;
        _analyzer = analyzer;
        _frames = frames;
        _settingsStore = settingsStore;
        _status = status;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscribed = true;
        AppBase.Current.AppStarted += OnAppStarted;
        AppBase.Current.AppStopping += OnAppStopping;
        _settingsStore.Settings.PropertyChanged += OnSettingsChanged;
        _capture.SamplesAvailable += OnSamplesAvailable;
        _capture.StateChanged += OnCaptureStateChanged;
        _analyzer.FrameProduced += OnFrameProduced;
        return Task.CompletedTask;
    }

    private async void OnAppStarted(object? sender, EventArgs eventArgs)
    {
        _appStarted = true;
        await ApplyEnabledStateAsync(_settingsStore.Settings.IsEnabled);
    }

    private async void OnAppStopping(object? sender, EventArgs eventArgs)
    {
        _appStarted = false;
        await StopAsync();
    }

    private async void OnSettingsChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(RhythmIslandSettings.IsEnabled) && _appStarted)
            await ApplyEnabledStateAsync(_settingsStore.Settings.IsEnabled);
    }

    public async Task ApplyEnabledStateAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!enabled) { await StopCoreAsync(cancellationToken); return; }
            if (_analysisCancellation is not null) return;

            _frames.Clear();
            UpdateStatus(() => { _status.State = RuntimeState.Starting; _status.LastError = "无"; });
            _channel = Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(8)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
            _analysisCancellation = new CancellationTokenSource();
            _analysisTask = RunAnalysisAsync(_channel.Reader, _analysisCancellation.Token);
            await _capture.StartAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "启动律动岛运行服务失败。");
            UpdateStatus(() => { _status.State = RuntimeState.Faulted; _status.LastError = exception.Message; });
            await StopCoreAsync(CancellationToken.None);
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { await StopCoreAsync(cancellationToken); }
        finally { _gate.Release(); }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        var cancellation = _analysisCancellation;
        _analysisCancellation = null;
        cancellation?.Cancel();
        _channel?.Writer.TryComplete();
        await _capture.StopAsync(cancellationToken);
        if (_analysisTask is not null)
        {
            try { await _analysisTask; } catch (OperationCanceledException) { }
        }
        cancellation?.Dispose();
        _analysisTask = null;
        _channel = null;
        _analyzer.Reset();
        _frames.Clear();
        UpdateStatus(() =>
        {
            _status.State = RuntimeState.Stopped;
            _status.DeviceName = "未连接";
            _status.Peak = 0;
        });
    }

    private void OnSamplesAvailable(object? sender, AudioSamplesEventArgs eventArgs) =>
        _channel?.Writer.TryWrite(new AudioChunk(eventArgs.Samples, eventArgs.SampleRate));

    private async Task RunAnalysisAsync(ChannelReader<AudioChunk> reader, CancellationToken cancellationToken)
    {
        var lastInput = DateTimeOffset.UtcNow;
        while (!cancellationToken.IsCancellationRequested)
        {
            var wait = reader.WaitToReadAsync(cancellationToken).AsTask();
            var tick = Task.Delay(33, cancellationToken);
            var completed = await Task.WhenAny(wait, tick);
            if (completed == wait && await wait)
            {
                while (reader.TryRead(out var chunk))
                {
                    var settings = _settingsStore.Settings;
                    _analyzer.Configure(chunk.SampleRate, 96, settings.Sensitivity, settings.Smoothing);
                    _analyzer.PushSamples(chunk.Samples);
                    lastInput = DateTimeOffset.UtcNow;
                }
            }
            else if (DateTimeOffset.UtcNow - lastInput > TimeSpan.FromMilliseconds(100))
            {
                _analyzer.AdvanceSilence();
            }
        }
    }

    private void OnCaptureStateChanged(object? sender, AudioCaptureStateEventArgs eventArgs) => UpdateStatus(() =>
    {
        _status.State = eventArgs.State;
        if (!string.IsNullOrWhiteSpace(eventArgs.DeviceName)) _status.DeviceName = eventArgs.DeviceName;
        if (eventArgs.Error is not null) _status.LastError = eventArgs.Error.Message;
    });

    private void OnFrameProduced(object? sender, SpectrumFrame frame)
    {
        _frames.Publish(frame);
        UpdateStatus(() => { _status.LastFrameAt = frame.GeneratedAt; _status.Peak = frame.Peak; });
    }

    private static void UpdateStatus(Action update)
    {
        if (Dispatcher.UIThread.CheckAccess()) update(); else Dispatcher.UIThread.Post(update);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_subscribed && AppBase.Current is not null)
        {
            AppBase.Current.AppStarted -= OnAppStarted;
            AppBase.Current.AppStopping -= OnAppStopping;
        }
        _settingsStore.Settings.PropertyChanged -= OnSettingsChanged;
        _capture.SamplesAvailable -= OnSamplesAvailable;
        _capture.StateChanged -= OnCaptureStateChanged;
        _analyzer.FrameProduced -= OnFrameProduced;
        _gate.Dispose();
    }
}
