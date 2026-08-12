using RhythmIsland.Models;

namespace RhythmIsland.Abstractions;

public sealed class AudioSamplesEventArgs(float[] samples, int sampleRate) : EventArgs
{
    public float[] Samples { get; } = samples;
    public int SampleRate { get; } = sampleRate;
}

public sealed class AudioCaptureStateEventArgs(RuntimeState state, string? deviceName = null, Exception? error = null) : EventArgs
{
    public RuntimeState State { get; } = state;
    public string? DeviceName { get; } = deviceName;
    public Exception? Error { get; } = error;
}

public interface IAudioCaptureService : IAsyncDisposable
{
    event EventHandler<AudioSamplesEventArgs>? SamplesAvailable;
    event EventHandler<AudioCaptureStateEventArgs>? StateChanged;
    RuntimeState State { get; }
    string? DeviceName { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
