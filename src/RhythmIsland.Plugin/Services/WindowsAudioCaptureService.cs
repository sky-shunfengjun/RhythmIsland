using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using RhythmIsland.Abstractions;
using RhythmIsland.Models;

namespace RhythmIsland.Services;

public sealed class WindowsAudioCaptureService : IAudioCaptureService, IMMNotificationClient
{
    private readonly ILogger<WindowsAudioCaptureService> _logger;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly CaptureFaultLatch _captureFault = new();
    private readonly CaptureInstanceGuard _captureInstances = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private WasapiLoopbackCapture? _capture;
    private long _captureInstanceId;
    private EventHandler<WaveInEventArgs>? _dataAvailableHandler;
    private EventHandler<StoppedEventArgs>? _recordingStoppedHandler;
    private CancellationTokenSource? _runCancellation;
    private SingleFlightReconnectCoordinator? _reconnectCoordinator;
    private string? _currentDeviceId;
    private volatile bool _stopping;

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
            cancellationToken.ThrowIfCancellationRequested();
            _runCancellation = new CancellationTokenSource();
            _reconnectCoordinator = new SingleFlightReconnectCoordinator(
                RestartCaptureAsync,
                _runCancellation.Token);
            _enumerator = new MMDeviceEnumerator();
            _enumerator.RegisterEndpointNotificationCallback(this);
            SetState(RuntimeState.Starting);
            if (!StartCaptureCore(_runCancellation.Token)) ScheduleRestart();
        }
        finally { _lifecycleGate.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? runCancellation;
        SingleFlightReconnectCoordinator? reconnectCoordinator;
        _stopping = true;
        await _lifecycleGate.WaitAsync(CancellationToken.None);
        try
        {
            runCancellation = _runCancellation;
            _runCancellation = null;
            reconnectCoordinator = _reconnectCoordinator;
            _reconnectCoordinator = null;
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

        if (reconnectCoordinator is not null)
        {
            try { await reconnectCoordinator.Completion.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None); }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }
        }
        runCancellation?.Dispose();
    }

    private bool StartCaptureCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DisposeCapture();
        DeviceName = null;
        string? selectedDeviceName = null;
        try
        {
            _device = _enumerator!.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            Volatile.Write(ref _currentDeviceId, _device.ID);
            selectedDeviceName = _device.FriendlyName;
            DeviceName = selectedDeviceName;
            var capture = new WasapiLoopbackCapture(_device);
            _capture = capture;
            var instanceId = _captureInstances.Begin(capture);
            var waveFormat = capture.WaveFormat;
            _captureInstanceId = instanceId;
            _dataAvailableHandler = (_, eventArgs) => OnDataAvailable(instanceId, capture, waveFormat, eventArgs);
            _recordingStoppedHandler = (_, eventArgs) => OnRecordingStopped(instanceId, capture, eventArgs);
            capture.DataAvailable += _dataAvailableHandler;
            capture.RecordingStopped += _recordingStoppedHandler;
            capture.StartRecording();
            _captureFault.Reset();
            SetState(RuntimeState.Running, DeviceName);
            _logger.LogInformation("默认扬声器回环捕获已启动：{DeviceName}", DeviceName);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "无法启动默认扬声器回环捕获。");
            DisposeCapture();
            DeviceName = selectedDeviceName;
            SetState(RuntimeState.DeviceUnavailable, selectedDeviceName, exception);
            return false;
        }
    }

    private void OnDataAvailable(
        long instanceId,
        WasapiLoopbackCapture source,
        WaveFormat waveFormat,
        WaveInEventArgs eventArgs)
    {
        if (!_captureInstances.IsCurrent(instanceId, source)) return;
        if (_captureFault.IsFaulted) return;
        try
        {
            if (eventArgs.BytesRecorded == 0) return;
            var mono = AudioSampleConverter.ConvertToMono(eventArgs.Buffer, eventArgs.BytesRecorded, waveFormat);
            if (mono.Length > 0 && _captureInstances.IsCurrent(instanceId, source))
                SamplesAvailable?.Invoke(this, new AudioSamplesEventArgs(mono, waveFormat.SampleRate));
        }
        catch (Exception exception)
        {
            if (!_captureFault.TryEnterFault()) return;
            _logger.LogError(exception, "转换扬声器音频数据失败。");
            SetState(RuntimeState.Faulted, DeviceName, exception);
            ScheduleRestart();
        }
    }

    private void OnRecordingStopped(long instanceId, WasapiLoopbackCapture source, StoppedEventArgs eventArgs)
    {
        if (!_captureInstances.IsCurrent(instanceId, source)) return;
        if (_stopping || _runCancellation is null) return;
        if (eventArgs.Exception is not null) _logger.LogWarning(eventArgs.Exception, "扬声器捕获意外停止。");
        SetState(RuntimeState.DeviceUnavailable, DeviceName, eventArgs.Exception);
        ScheduleRestart();
    }

    private void ScheduleRestart(TimeSpan? debounce = null)
    {
        if (_stopping) return;
        _reconnectCoordinator?.Request(debounce);
    }

    private async Task<bool> RestartCaptureAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_stopping || _runCancellation is null || cancellationToken != _runCancellation.Token) return true;
            DeviceName = null;
            SetState(RuntimeState.Starting);
            return StartCaptureCore(cancellationToken);
        }
        finally { _lifecycleGate.Release(); }
    }

    private void DisposeCapture()
    {
        var capture = _capture;
        var instanceId = _captureInstanceId;
        var dataAvailableHandler = _dataAvailableHandler;
        var recordingStoppedHandler = _recordingStoppedHandler;
        _capture = null;
        _captureInstanceId = 0;
        _dataAvailableHandler = null;
        _recordingStoppedHandler = null;
        _captureInstances.Clear(instanceId, capture);
        if (capture is not null)
        {
            if (dataAvailableHandler is not null) capture.DataAvailable -= dataAvailableHandler;
            if (recordingStoppedHandler is not null) capture.RecordingStopped -= recordingStoppedHandler;
            try { capture.StopRecording(); } catch { }
            capture.Dispose();
        }
        _device?.Dispose();
        _device = null;
        Volatile.Write(ref _currentDeviceId, null);
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
        if (Volatile.Read(ref _currentDeviceId) == deviceId) ScheduleRestart(TimeSpan.FromMilliseconds(250));
    }
    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        if (Volatile.Read(ref _currentDeviceId) == deviceId && newState != DeviceState.Active)
            ScheduleRestart(TimeSpan.FromMilliseconds(250));
    }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycleGate.Dispose();
    }
}
