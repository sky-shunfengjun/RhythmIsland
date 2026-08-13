namespace RhythmIsland.Services;

internal interface ISystemMediaSessionBackend : IDisposable
{
    event EventHandler? Changed;
    Task<byte[]?> ReadCurrentThumbnailAsync(int maximumBytes, CancellationToken cancellationToken);
}
