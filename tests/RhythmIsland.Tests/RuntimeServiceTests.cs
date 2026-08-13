using Microsoft.Extensions.Logging.Abstractions;
using RhythmIsland.Abstractions;
using RhythmIsland.Models;
using RhythmIsland.Services;

namespace RhythmIsland.Tests;

public sealed class RuntimeServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RhythmIsland.Runtime.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RepeatedEnableUsesOnlyOneCaptureInstance()
    {
        var capture = new FakeCapture();
        using var store = CreateStore();
        using var runtime = CreateRuntime(capture, store);

        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => runtime.ApplyEnabledStateAsync(true)));
        Assert.Equal(1, capture.StartCount);
        Assert.Equal(1, capture.ActiveCount);
        Assert.Equal(1, capture.MaximumActiveCount);

        await runtime.StopAsync();
        Assert.Equal(0, capture.ActiveCount);
    }

    [Fact]
    public async Task HostedStartImmediatelyAppliesPersistedEnabledStateWithoutWaitingForAppStartedEvent()
    {
        var capture = new FakeCapture();
        using var store = CreateStore();
        store.Settings.IsEnabled = true;
        using var runtime = CreateRuntime(capture, store);

        runtime.MarkApplicationReady();
        await runtime.CoordinationTask;

        Assert.Equal(1, capture.StartCount);
        Assert.Equal(1, capture.ActiveCount);
        await runtime.StopAsync();
    }

    [Fact]
    public async Task RestartCaptureStopsThenStartsWithoutOverlap()
    {
        var capture = new FakeCapture();
        using var store = CreateStore();
        store.Settings.IsEnabled = true;
        using var runtime = CreateRuntime(capture, store);

        await runtime.ApplyEnabledStateAsync(true);
        var restarted = await runtime.RestartCaptureAsync();

        Assert.True(restarted);
        Assert.Equal(2, capture.StartCount);
        Assert.Equal(1, capture.RealStopCount);
        Assert.Equal(1, capture.ActiveCount);
        Assert.Equal(1, capture.MaximumActiveCount);
        await runtime.StopAsync();
    }

    [Fact]
    public async Task RestartCaptureDoesNothingWhenMasterSwitchIsOff()
    {
        var capture = new FakeCapture();
        using var store = CreateStore();
        using var runtime = CreateRuntime(capture, store);

        Assert.False(await runtime.RestartCaptureAsync());
        Assert.Equal(0, capture.StartCount);
    }

    [Fact]
    public async Task EnableDisableStormNeverOverlapsCaptureAndStopIsFinal()
    {
        var capture = new FakeCapture();
        using var store = CreateStore();
        using var runtime = CreateRuntime(capture, store);

        for (var i = 0; i < 20; i++)
        {
            await runtime.ApplyEnabledStateAsync(true);
            capture.EmitSamples(new float[2048]);
            await runtime.ApplyEnabledStateAsync(false);
        }
        await runtime.StopAsync();
        capture.EmitSamples(new float[2048]);

        Assert.Equal(1, capture.MaximumActiveCount);
        Assert.Equal(0, capture.ActiveCount);
        Assert.Equal(capture.StartCount, capture.RealStopCount);
    }

    [Fact]
    public async Task StartCancellationCleansUpAndDoesNotLeaveCaptureRunning()
    {
        var capture = new FakeCapture { StartDelay = TimeSpan.FromSeconds(5) };
        using var store = CreateStore();
        using var runtime = CreateRuntime(capture, store);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runtime.ApplyEnabledStateAsync(true, cancellation.Token));

        Assert.Equal(0, capture.ActiveCount);
    }

    [Fact]
    public async Task SilentAnalysisUsesOneTaskAndStopsPromptly()
    {
        var capture = new FakeCapture();
        var analyzer = new FakeAnalyzer();
        using var store = CreateStore();
        using var runtime = CreateRuntime(capture, store, analyzer);

        await runtime.ApplyEnabledStateAsync(true);
        Assert.Equal(1, runtime.ActiveAnalysisTaskCount);

        await WaitUntilAsync(() => analyzer.AdvanceSilenceCount > 0, TimeSpan.FromSeconds(10));
        Assert.True(analyzer.AdvanceSilenceCount > 0);
        Assert.Equal(1, runtime.ActiveAnalysisTaskCount);

        await runtime.StopAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(0, runtime.ActiveAnalysisTaskCount);
    }

    [Fact]
    public async Task AnalysisFailureStopsCaptureAndStillAllowsFinalCleanup()
    {
        var capture = new FakeCapture();
        var analyzer = new FakeAnalyzer { ThrowOnAdvanceSilence = true };
        using var store = CreateStore();
        using var runtime = CreateRuntime(capture, store, analyzer);

        await runtime.ApplyEnabledStateAsync(true);
        await WaitUntilAsync(() => analyzer.AdvanceSilenceCount > 0 && capture.ActiveCount == 0,
            TimeSpan.FromSeconds(10));

        Assert.True(analyzer.AdvanceSilenceCount > 0);
        Assert.Equal(0, capture.ActiveCount);
        await runtime.StopAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(0, runtime.ActiveAnalysisTaskCount);
    }

    [Fact]
    public async Task SingleFlightReconnectUsesBackoffAndCoalescesStorm()
    {
        using var cancellation = new CancellationTokenSource();
        var delays = new List<TimeSpan>();
        var firstDelayReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var coordinator = new SingleFlightReconnectCoordinator(
            _ => Task.FromResult(++attempts >= 4),
            cancellation.Token,
            async (delay, _) =>
            {
                delays.Add(delay);
                if (delays.Count == 1)
                {
                    firstDelayReached.SetResult();
                    await releaseFirstDelay.Task;
                }
            });

        Assert.True(coordinator.Request());
        await firstDelayReached.Task;
        Assert.False(coordinator.Request());
        Assert.False(coordinator.Request(TimeSpan.FromMilliseconds(250)));
        releaseFirstDelay.SetResult();
        await coordinator.Completion;

        Assert.Equal(4, attempts);
        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)],
            delays);

        Assert.True(coordinator.Request());
        await coordinator.Completion;
        Assert.Equal(5, attempts);
        Assert.Equal(TimeSpan.FromSeconds(1), delays[^1]);
    }

    [Fact]
    public async Task SingleFlightReconnectStopsAfterCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var coordinator = new SingleFlightReconnectCoordinator(
            _ => { attempts++; return Task.FromResult(false); },
            cancellation.Token,
            (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));

        Assert.True(coordinator.Request());
        cancellation.Cancel();
        await coordinator.Completion;
        Assert.Equal(0, attempts);
        Assert.False(coordinator.Request());
    }

    [Fact]
    public void CaptureFaultLatchReportsOnlyFirstFailureUntilReset()
    {
        var latch = new CaptureFaultLatch();
        Assert.True(latch.TryEnterFault());
        Assert.True(latch.IsFaulted);
        Assert.False(latch.TryEnterFault());

        latch.Reset();
        Assert.False(latch.IsFaulted);
        Assert.True(latch.TryEnterFault());
    }

    [Fact]
    public void CaptureInstanceGuardRejectsCallbacksFromPreviousCapture()
    {
        var guard = new CaptureInstanceGuard();
        var first = new object();
        var firstId = guard.Begin(first);
        Assert.True(guard.IsCurrent(firstId, first));

        var second = new object();
        var secondId = guard.Begin(second);
        Assert.False(guard.IsCurrent(firstId, first));
        Assert.False(guard.IsCurrent(firstId, second));
        Assert.True(guard.IsCurrent(secondId, second));

        guard.Clear(firstId, first);
        Assert.True(guard.IsCurrent(secondId, second));
        guard.Clear(secondId, second);
        Assert.False(guard.IsCurrent(secondId, second));
    }

    private RhythmIslandRuntimeService CreateRuntime(FakeCapture capture, RhythmIslandSettingsStore store,
        FakeAnalyzer? analyzer = null) =>
        new(capture, analyzer ?? new FakeAnalyzer(), new SpectrumFrameProvider(), store, new RuntimeStatus(),
            NullLogger<RhythmIslandRuntimeService>.Instance);

    private RhythmIslandSettingsStore CreateStore() => new(_folder, NullLogger<RhythmIslandSettingsStore>.Instance);

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private sealed class FakeCapture : IAudioCaptureService
    {
        public event EventHandler<AudioSamplesEventArgs>? SamplesAvailable;
        public event EventHandler<AudioCaptureStateEventArgs>? StateChanged;
        public RuntimeState State { get; private set; } = RuntimeState.Stopped;
        public string? DeviceName => "测试扬声器";
        public int StartCount { get; private set; }
        public int RealStopCount { get; private set; }
        public int ActiveCount { get; private set; }
        public int MaximumActiveCount { get; private set; }
        public TimeSpan StartDelay { get; init; }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (StartDelay > TimeSpan.Zero) await Task.Delay(StartDelay, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            ActiveCount++;
            MaximumActiveCount = Math.Max(MaximumActiveCount, ActiveCount);
            State = RuntimeState.Running;
            StateChanged?.Invoke(this, new AudioCaptureStateEventArgs(State, DeviceName));
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (ActiveCount > 0) { ActiveCount--; RealStopCount++; }
            State = RuntimeState.Stopped;
            StateChanged?.Invoke(this, new AudioCaptureStateEventArgs(State));
            return Task.CompletedTask;
        }

        public void EmitSamples(float[] samples) => SamplesAvailable?.Invoke(this, new AudioSamplesEventArgs(samples, 48000));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeAnalyzer : ISpectrumAnalyzer
    {
        private int _advanceSilenceCount;
        public int AdvanceSilenceCount => Volatile.Read(ref _advanceSilenceCount);
        public bool ThrowOnAdvanceSilence { get; init; }
        public event EventHandler<SpectrumFrame>? FrameProduced;
        public void Configure(int sampleRate, int barCount, double sensitivity, double smoothing) { }
        public void PushSamples(ReadOnlySpan<float> samples) =>
            FrameProduced?.Invoke(this, new SpectrumFrame(new float[48], DateTimeOffset.UtcNow, true));
        public void AdvanceSilence()
        {
            Interlocked.Increment(ref _advanceSilenceCount);
            if (ThrowOnAdvanceSilence) throw new InvalidOperationException("模拟分析失败");
        }
        public void Reset() { }
    }
}
