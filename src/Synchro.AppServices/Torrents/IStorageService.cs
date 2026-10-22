using Synchro.Core.Torrents;

namespace Synchro.AppServices.Torrents;

public interface IStorageService
{
    /// <summary>Disk space where the server downloads to by default; null when not connected.</summary>
    Task<StorageInfo?> GetDownloadStorageAsync(CancellationToken ct = default);
}
