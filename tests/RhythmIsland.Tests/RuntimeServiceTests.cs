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

    private RhythmIslandRuntimeService CreateRuntime(FakeCapture capture, RhythmIslandSettingsStore store) =>
        new(capture, new FakeAnalyzer(), new SpectrumFrameProvider(), store, new RuntimeStatus(),
            NullLogger<RhythmIslandRuntimeService>.Instance);

    private RhythmIslandSettingsStore CreateStore() => new(_folder, NullLogger<RhythmIslandSettingsStore>.Instance);

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

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            ActiveCount++;
            MaximumActiveCount = Math.Max(MaximumActiveCount, ActiveCount);
            State = RuntimeState.Running;
            StateChanged?.Invoke(this, new AudioCaptureStateEventArgs(State, DeviceName));
            return Task.CompletedTask;
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
        public event EventHandler<SpectrumFrame>? FrameProduced;
        public void Configure(int sampleRate, int barCount, double sensitivity, double smoothing) { }
        public void PushSamples(ReadOnlySpan<float> samples) =>
            FrameProduced?.Invoke(this, new SpectrumFrame(new float[48], DateTimeOffset.UtcNow, true));
        public void AdvanceSilence() { }
        public void Reset() { }
    }
}
