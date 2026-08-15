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
    internal static readonly TimeSpan AnalysisPollingInterval = TimeSpan.FromMilliseconds(8);
    internal static readonly TimeSpan SilenceDelay = TimeSpan.FromMilliseconds(100);
    internal static readonly TimeSpan SilenceAdvanceInterval = TimeSpan.FromSeconds(1d / 60);
    private sealed record AudioChunk(float[] Samples, int SampleRate);
    private readonly IAudioCaptureService _capture;
    private readonly ISpectrumAnalyzer _analyzer;
    private readonly ISpectrumFrameProvider _frames;
    private readonly RhythmIslandSettingsStore _settingsStore;
    private readonly RuntimeStatus _status;
    private readonly ILogger<RhythmIslandRuntimeService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _coordinationLock = new();
    private Channel<AudioChunk>? _channel;
    private CancellationTokenSource? _analysisCancellation;
    private Task? _analysisTask;
    private Task _coordinationTask = Task.CompletedTask;
    private bool _appStarted;
    private bool _hostStopping;
    private bool _subscribed;
    private bool _disposed;

    internal int ActiveAnalysisTaskCount => _analysisTask is { IsCompleted: false } ? 1 : 0;
    internal Task CoordinationTask
    {
        get { lock (_coordinationLock) return _coordinationTask; }
    }

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
        MarkApplicationReady();
        return Task.CompletedTask;
    }

    private void OnAppStarted(object? sender, EventArgs eventArgs) => MarkApplicationReady();

    internal void MarkApplicationReady()
    {
        if (_hostStopping || _disposed) return;
        _appStarted = true;
        QueueRuntimeReconciliation();
    }

    private void OnAppStopping(object? sender, EventArgs eventArgs)
    {
        _appStarted = false;
        _hostStopping = true;
        QueueRuntimeReconciliation();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(RhythmIslandSettings.IsEnabled) && _appStarted)
            QueueRuntimeReconciliation();
    }

    private void QueueRuntimeReconciliation()
    {
        lock (_coordinationLock)
        {
            var previous = _coordinationTask;
            _coordinationTask = ReconcileAfterAsync(previous);
        }
    }

    private async Task ReconcileAfterAsync(Task previous)
    {
        try
        {
            try { await previous; }
            catch { /* The preceding reconciliation has already logged its failure. */ }

            var shouldRun = _appStarted && !_hostStopping && _settingsStore.Settings.IsEnabled;
            await ApplyEnabledStateAsync(shouldRun, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "协调律动岛运行状态失败。");
        }
    }

    public async Task ApplyEnabledStateAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!enabled) { await StopCoreAsync(cancellationToken); return; }
            if (_analysisCancellation is not null) return;
            await StartCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopCoreAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "启动律动岛运行服务失败。");
            await StopCoreAsync(CancellationToken.None);
            UpdateStatus(() => { _status.State = RuntimeState.Faulted; _status.LastError = exception.Message; });
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> RestartCaptureAsync(CancellationToken cancellationToken = default)
    {
        if (!_settingsStore.Settings.IsEnabled || _hostStopping || _disposed) return false;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync(CancellationToken.None);
            if (!_settingsStore.Settings.IsEnabled || _hostStopping || _disposed) return false;
            await StartCoreAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopCoreAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "重新启动律动岛捕获失败。");
            await StopCoreAsync(CancellationToken.None);
            UpdateStatus(() => { _status.State = RuntimeState.Faulted; _status.LastError = exception.Message; });
            return false;
        }
        finally { _gate.Release(); }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        _frames.Clear();
        UpdateStatus(() => { _status.State = RuntimeState.Starting; _status.LastError = "无"; });
        _channel = Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(8)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _analysisCancellation = new CancellationTokenSource();
        _analysisTask = MonitorAnalysisAsync(_channel.Reader, _analysisCancellation.Token);
        await _capture.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _hostStopping = true;
        _appStarted = false;
        Task coordination;
        lock (_coordinationLock) coordination = _coordinationTask;
        try { await coordination.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { }

        await _gate.WaitAsync(CancellationToken.None);
        try { await StopCoreAsync(CancellationToken.None); }
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
        var nextSilenceAdvance = lastInput + SilenceDelay;
        using var timer = new PeriodicTimer(AnalysisPollingInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var receivedInput = false;
            while (reader.TryRead(out var chunk))
            {
                var settings = _settingsStore.Settings;
                _analyzer.Configure(chunk.SampleRate, 96, settings.Sensitivity, settings.Smoothing);
                _analyzer.PushSamples(chunk.Samples);
                receivedInput = true;
            }

            var now = DateTimeOffset.UtcNow;
            if (receivedInput)
            {
                lastInput = now;
                nextSilenceAdvance = now + SilenceDelay;
            }
            else if (now - lastInput >= SilenceDelay && now >= nextSilenceAdvance)
            {
                _analyzer.AdvanceSilence();
                do { nextSilenceAdvance += SilenceAdvanceInterval; }
                while (nextSilenceAdvance <= now);
            }
        }
    }

    private async Task MonitorAnalysisAsync(ChannelReader<AudioChunk> reader, CancellationToken cancellationToken)
    {
        try
        {
            await RunAnalysisAsync(reader, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "律动岛频谱分析任务意外停止。");
            UpdateStatus(() =>
            {
                _status.State = RuntimeState.Faulted;
                _status.LastError = exception.Message;
            });
            QueueAnalysisFailureCleanup(exception);
        }
    }

    private void QueueAnalysisFailureCleanup(Exception failure)
    {
        lock (_coordinationLock)
        {
            var previous = _coordinationTask;
            _coordinationTask = CleanupAfterAnalysisFailureAsync(previous, failure);
        }
    }

    private async Task CleanupAfterAnalysisFailureAsync(Task previous, Exception failure)
    {
        try
        {
            try { await previous; }
            catch { /* The preceding coordination task has already logged its failure. */ }

            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                await StopCoreAsync(CancellationToken.None);
                UpdateStatus(() =>
                {
                    _status.State = RuntimeState.Faulted;
                    _status.LastError = failure.Message;
                });
            }
            finally { _gate.Release(); }
        }
        catch (Exception cleanupException)
        {
            _logger.LogError(cleanupException, "清理已故障的律动岛分析任务失败。");
        }
    }

    private void OnCaptureStateChanged(object? sender, AudioCaptureStateEventArgs eventArgs) =>
        UpdateStatus(() => ApplyCaptureState(_status, eventArgs));

    internal static void ApplyCaptureState(RuntimeStatus status, AudioCaptureStateEventArgs eventArgs)
    {
        status.State = eventArgs.State;
        if (!string.IsNullOrWhiteSpace(eventArgs.DeviceName))
            status.DeviceName = eventArgs.DeviceName;
        else if (eventArgs.State is RuntimeState.Starting or RuntimeState.Stopped or RuntimeState.DeviceUnavailable)
            status.DeviceName = "未连接";
        if (eventArgs.Error is not null) status.LastError = eventArgs.Error.Message;
    }

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
