using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RhythmIsland.Services;
using RhythmIsland.Models;

namespace RhythmIsland.Tests;

public sealed class ServiceLifecycleRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LastSettingChangeDuringWriteMustReachStorage(bool exitBeforeNextSave)
    {
        using var writer = new BlockingWriter();
        using var store = new RhythmIslandSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            NullLogger<RhythmIslandSettingsStore>.Instance, TimeSpan.FromMilliseconds(5), Task.Delay, writer);
        store.Settings.BackgroundSpectrum.Amplitude = 1.5;
        await writer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { store.Settings.BackgroundSpectrum.Amplitude = 2.0; }
        finally { writer.Release.Set(); }
        await writer.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (!exitBeforeNextSave) await writer.SecondWrite.Task.WaitAsync(TimeSpan.FromSeconds(5));
        store.Dispose();
        using var stored = JsonDocument.Parse(writer.LastContents!);
        Assert.Equal(2.0, stored.RootElement.GetProperty("BackgroundSpectrum").GetProperty("Amplitude").GetDouble());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentCoverInitializationAndReleaseMustNotThrow(bool disposeService)
    {
        for (var iteration = 0; iteration < 3000; iteration++)
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ready = new TaskCompletionSource<ISystemMediaSessionBackend>(TaskCreationOptions.RunContinuationsAsynchronously);
            var service = new SystemMediaCoverService(NullLogger<SystemMediaCoverService>.Instance, () => true,
                _ => { entered.TrySetResult(); return ready.Task; });
            var lease = service.Acquire();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Exception? failure = null;
            await Task.WhenAll(Task.Run(() =>
                {
                    try
                    {
                        if (disposeService) service.Dispose();
                        else lease.Dispose();
                    }
                    catch (Exception e) { failure = e; }
                }),
                Task.Run(() => ready.TrySetResult(new Backend())));
            try { service.Dispose(); } catch (Exception e) { failure ??= e; }
            lease.Dispose();
            Assert.Null(failure);
        }
    }

    [Fact]
    public async Task CancellationDoesNotHoldLifetimeLockWhileRunningCallbacks()
    {
        using var lifetime = new CancellationLifetime();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.Token.Register(() =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
        });
        var cancellation = Task.Run(lifetime.Cancel);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Run(lifetime.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(lifetime.Token.IsCancellationRequested);
        }
        finally
        {
            release.Set();
            await cancellation.WaitAsync(TimeSpan.FromSeconds(5));
        }
        lifetime.Cancel();
    }

    [Fact]
    public void FinishedCancellationLifetimeIgnoresLateCancellation()
    {
        using var lifetime = new CancellationLifetime();
        lifetime.Dispose();
        lifetime.Cancel();
        Assert.False(lifetime.Token.IsCancellationRequested);
    }

    private sealed class BlockingWriter : ISettingsFileWriter, IDisposable
    {
        private int _writeCount;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public byte[]? LastContents;
        public void Write(string path, byte[] contents)
        {
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            LastContents = contents;
            if (Interlocked.Increment(ref _writeCount) >= 2) SecondWrite.TrySetResult();
            Finished.TrySetResult();
        }
        public void Dispose() => Release.Dispose();
    }
    private sealed class Backend : ISystemMediaSessionBackend
    {
        public event EventHandler? Changed { add { } remove { } }
        public Task<byte[]?> ReadCurrentThumbnailAsync(int maximumBytes, CancellationToken token) => Task.FromResult<byte[]?>(null);
        public void Dispose() { }
    }
}
