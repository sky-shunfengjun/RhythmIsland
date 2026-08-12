namespace RhythmIsland.Abstractions;

public interface IRhythmIslandRuntimeService
{
    Task ApplyEnabledStateAsync(bool enabled, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
