using Synchro.AppServices.Connection;
using Synchro.Core.Torrents;

namespace Synchro.AppServices.Torrents;

public sealed class AddTorrentService(IConnectionService connection, ITorrentPoller poller) : IAddTorrentService
{
    public async Task<AddTorrentResult> AddAsync(AddTorrentRequest request, CancellationToken ct = default)
    {
        var result = await Client().AddTorrentAsync(request, ct);

        // Show the new torrent now instead of at the next poll: just that one, not the whole list.
        if (!string.IsNullOrEmpty(result.HashString))
            poller.RequestRefresh([result.HashString]);
        return result;
    }

    public Task<string> GetDefaultDownloadDirectoryAsync(CancellationToken ct = default)
        => Client().GetDefaultDownloadDirectoryAsync(ct);

    public Task<long> GetFreeSpaceAsync(string path, CancellationToken ct = default)
        => Client().GetFreeSpaceAsync(path, ct);

    public Task<TorrentFileList?> GetFileListAsync(string hash, CancellationToken ct = default)
        => Client().GetFileListAsync(hash, ct);

    public async Task FinishAddAsync(string hash, FileChoices? choices, bool start, CancellationToken ct = default)
    {
        var client = Client();
        if (choices is not null)
        {
            // Each call returns early for an empty list (an empty list would mean "all files").
            await client.SetFilesWantedAsync(hash, choices.Unwanted, wanted: false, ct);
            await client.SetFilePriorityAsync(hash, choices.HighPriority, FilePriority.High, ct);
            await client.SetFilePriorityAsync(hash, choices.LowPriority, FilePriority.Low, ct);
        }

        if (start)
            await client.StartAsync([hash], startNow: false, ct);
        else
            await client.StopAsync([hash], ct);

        poller.RequestRefresh([hash]);
    }

    public Task HoldForChoiceAsync(string hash, CancellationToken ct = default) => Client().StopAsync([hash], ct);

    public async Task UndoAddAsync(string hash, CancellationToken ct = default)
    {
        await Client().RemoveAsync([hash], deleteLocalData: false, ct);
        poller.RequestRefresh();
    }

    private ITransmissionClient Client()
        => connection.Client ?? throw new InvalidOperationException("Not connected to a server.");
}
