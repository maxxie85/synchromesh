using Synchro.AppServices.Connection;
using Synchro.Core.Torrents;

namespace Synchro.AppServices.Torrents;

public sealed class TorrentActions(IConnectionService connection, ITorrentPoller poller) : ITorrentActions
{
    public Task StartAsync(IReadOnlyList<string> hashes, bool startNow = false, CancellationToken ct = default)
        => RunAsync(hashes, client => client.StartAsync(hashes, startNow, ct));

    public Task StopAsync(IReadOnlyList<string> hashes, CancellationToken ct = default)
        => RunAsync(hashes, client => client.StopAsync(hashes, ct));

    public Task VerifyAsync(IReadOnlyList<string> hashes, CancellationToken ct = default)
        => RunAsync(hashes, client => client.VerifyAsync(hashes, ct));

    public Task ReannounceAsync(IReadOnlyList<string> hashes, CancellationToken ct = default)
        => RunAsync(hashes, client => client.ReannounceAsync(hashes, ct));

    public Task RemoveAsync(IReadOnlyList<string> hashes, bool deleteLocalData, CancellationToken ct = default)
        => RunAsync(hashes, client => client.RemoveAsync(hashes, deleteLocalData, ct), everything: true);

    public Task MoveInQueueAsync(IReadOnlyList<string> hashes, QueueMove move, CancellationToken ct = default)
        => RunAsync(hashes, client => client.MoveInQueueAsync(hashes, move, ct), everything: true);

    // Torrents that end up with the same labels share one request.
    public Task SetLabelsAsync(IReadOnlyDictionary<string, IReadOnlyList<string>> labelsByHash, CancellationToken ct = default)
        => RunAsync(labelsByHash.Keys.ToList(), async client =>
        {
            foreach (var group in labelsByHash.GroupBy(p => string.Join('\n', p.Value)))
                await client.SetLabelsAsync(group.Select(p => p.Key).ToList(), group.First().Value, ct);
        });

    public Task SetTrackersAsync(string hash, IReadOnlyList<IReadOnlyList<string>> tiers, CancellationToken ct = default)
        => RunAsync([hash], client => client.SetTrackersAsync(hash, tiers, ct));

    public Task RenamePathAsync(string hash, string path, string newName, CancellationToken ct = default)
        => RunAsync([hash], client => client.RenamePathAsync(hash, path, newName, ct));

    public Task SetOptionsAsync(string hash, TorrentOptions options, CancellationToken ct = default)
        => RunAsync([hash], client => client.SetTorrentOptionsAsync(hash, options, ct));

    public Task MoveDataAsync(IReadOnlyList<string> hashes, string location, bool moveFiles, CancellationToken ct = default)
        => RunAsync(hashes, client => client.SetLocationAsync(hashes, location, moveFiles, ct));

    public async Task<string?> GetLocationAsync(string hash, CancellationToken ct = default)
    {
        var client = connection.Client ?? throw new InvalidOperationException("Not connected to a server.");
        return (await client.GetTorrentDetailsAsync(hash, ct: ct))?.DownloadDirectory;
    }

    public Task SetFilesWantedAsync(string hash, IReadOnlyList<int> fileIndices, bool wanted, CancellationToken ct = default)
        => fileIndices.Count == 0
            ? Task.CompletedTask
            : RunAsync([hash], client => client.SetFilesWantedAsync(hash, fileIndices, wanted, ct));

    public Task SetFilePriorityAsync(string hash, IReadOnlyList<int> fileIndices, FilePriority priority, CancellationToken ct = default)
        => fileIndices.Count == 0
            ? Task.CompletedTask
            : RunAsync([hash], client => client.SetFilePriorityAsync(hash, fileIndices, priority, ct));

    /// <param name="everything">
    /// Re-read the whole list afterwards: for removing (gone torrents) and queue moves (other torrents shift too).
    /// Otherwise only the touched torrents are re-read: a delta poll alone wouldn't show the change (a stopped
    /// torrent is no longer "recently active"), and on a big server the whole list is megabytes.
    /// </param>
    private async Task RunAsync(IReadOnlyList<string> hashes, Func<ITransmissionClient, Task> action, bool everything = false)
    {
        if (hashes.Count == 0) return;

        var client = connection.Client ?? throw new InvalidOperationException("Not connected to a server.");
        await action(client);

        if (everything)
            poller.RequestRefresh();
        else
            poller.RequestRefresh(hashes);
    }
}
