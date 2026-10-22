using Synchro.AppServices.Connection;
using Synchro.Core.Torrents;

namespace Synchro.AppServices.Torrents;

public sealed class StorageService(IConnectionService connection) : IStorageService
{
    public async Task<StorageInfo?> GetDownloadStorageAsync(CancellationToken ct = default)
    {
        if (connection.Client is not { } client) return null;

        var folder = await client.GetDefaultDownloadDirectoryAsync(ct);
        return string.IsNullOrWhiteSpace(folder) ? null : await client.GetStorageAsync(folder, ct);
    }
}
