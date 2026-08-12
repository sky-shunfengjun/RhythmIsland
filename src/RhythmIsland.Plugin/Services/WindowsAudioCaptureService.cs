using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using RhythmIsland.Abstractions;
using RhythmIsland.Models;

namespace RhythmIsland.Services;

public sealed class WindowsAudioCaptureService : IAudioCaptureService, IMMNotificationClient
{
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];
    private readonly ILogger<WindowsAudioCaptureService> _logger;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private WasapiLoopbackCapture? _capture;
    private CancellationTokenSource? _runCancellation;
    private Task? _restartTask;
    private int _retryIndex;
    private bool _stopping;

    public WindowsAudioCaptureService(ILogger<WindowsAudioCaptureService> logger) => _logger = logger;

    public event EventHandler<AudioSamplesEventArgs>? SamplesAvailable;
    public event EventHandler<AudioCaptureStateEventArgs>? StateChanged;
    public RuntimeState State { get; private set; } = RuntimeState.Stopped;
    public string? DeviceName { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_runCancellation is not null) return;
            _stopping = false;
            _retryIndex = 0;
            cancellationToken.ThrowIfCancellationRequested();
            _runCancellation = new CancellationTokenSource();
            _enumerator = new MMDeviceEnumerator();
            _enumerator.RegisterEndpointNotificationCallback(this);
            SetState(RuntimeState.Starting);
            await StartCaptureCoreAsync(_runCancellation.Token, scheduleOnFailure: true);
        }
        finally { _lifecycleGate.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? runCancellation;
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            _stopping = true;
            runCancellation = _runCancellation;
            _runCancellation = null;
            runCancellation?.Cancel();
            DisposeCapture();
            if (_enumerator is not null)
            {
                try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
                _enumerator.Dispose();
                _enumerator = null;
            }
            DeviceName = null;
            SetState(RuntimeState.Stopped);
        }
        finally { _lifecycleGate.Release(); }

        if (_restartTask is not null)
        {
            try { await _restartTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken); }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }
        }
        runCancellation?.Dispose();
    }

    private Task StartCaptureCoreAsync(CancellationToken cancellationToken, bool scheduleOnFailure)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DisposeCapture();
        try
        {
            _device = _enumerator!.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            DeviceName = _device.FriendlyName;
            var capture = new WasapiLoopbackCapture(_device);
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            _capture = capture;
            capture.StartRecording();
            _retryIndex = 0;
            SetState(RuntimeState.Running, DeviceName);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "无法启动默认扬声器回环捕获。");
            DisposeCapture();
            SetState(RuntimeState.DeviceUnavailable, null, exception);
            if (scheduleOnFailure) ScheduleRestart();
        }
        return Task.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        try
        {
            var capture = _capture;
            if (capture is null || eventArgs.BytesRecorded == 0) return;
            var mono = AudioSampleConverter.ConvertToMono(eventArgs.Buffer, eventArgs.BytesRecorded, capture.WaveFormat);
            if (mono.Length > 0) SamplesAvailable?.Invoke(this, new AudioSamplesEventArgs(mono, capture.WaveFormat.SampleRate));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "转换扬声器音频数据失败。");
            SetState(RuntimeState.Faulted, DeviceName, exception);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (_stopping || _runCancellation is null) return;
        if (eventArgs.Exception is not null) _logger.LogWarning(eventArgs.Exception, "扬声器捕获意外停止。");
        SetState(RuntimeState.DeviceUnavailable, DeviceName, eventArgs.Exception);
        ScheduleRestart();
    }

    private void ScheduleRestart(TimeSpan? debounce = null)
    {
        var cancellation = _runCancellation;
        if (_stopping || cancellation is null || cancellation.IsCancellationRequested) return;
        if (_restartTask is { IsCompleted: false }) return;
        _restartTask = Task.Run(async () =>
        {
            try
            {
                var firstDelay = debounce;
                while (!_stopping && ReferenceEquals(cancellation, _runCancellation))
                {
                    var delay = firstDelay ?? RetryDelays[Math.Min(_retryIndex++, RetryDelays.Length - 1)];
                    firstDelay = null;
                    await Task.Delay(delay, cancellation.Token);
                    await _lifecycleGate.WaitAsync(cancellation.Token);
                    try
                    {
                        SetState(RuntimeState.Starting);
                        await StartCaptureCoreAsync(cancellation.Token, scheduleOnFailure: false);
                        if (State == RuntimeState.Running) return;
                    }
                    finally { _lifecycleGate.Release(); }
                }
            }
            catch (OperationCanceledException) { }
        }, cancellation.Token);
    }

    private void DisposeCapture()
    {
        var capture = _capture;
        _capture = null;
        if (capture is not null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            try { capture.StopRecording(); } catch { }
            capture.Dispose();
        }
        _device?.Dispose();
        _device = null;
    }

    private void SetState(RuntimeState state, string? deviceName = null, Exception? error = null)
    {
        State = state;
        if (deviceName is not null) DeviceName = deviceName;
        StateChanged?.Invoke(this, new AudioCaptureStateEventArgs(state, DeviceName, error));
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia) ScheduleRestart(TimeSpan.FromMilliseconds(250));
    }
    public void OnDeviceRemoved(string deviceId)
    {
        if (_device?.ID == deviceId) ScheduleRestart(TimeSpan.FromMilliseconds(250));
    }
    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        if (_device?.ID == deviceId && newState != DeviceState.Active) ScheduleRestart(TimeSpan.FromMilliseconds(250));
    }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycleGate.Dispose();
    }
}
