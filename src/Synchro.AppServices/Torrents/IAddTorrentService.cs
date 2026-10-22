using Synchro.Core.Torrents;

namespace Synchro.AppServices.Torrents;

public interface IAddTorrentService
{
    Task<AddTorrentResult> AddAsync(AddTorrentRequest request, CancellationToken ct = default);
    Task<string> GetDefaultDownloadDirectoryAsync(CancellationToken ct = default);
    Task<long> GetFreeSpaceAsync(string path, CancellationToken ct = default);

    /// <summary>The file list of a just-added torrent; see <see cref="TorrentFileList.HasMetadata"/>.</summary>
    Task<TorrentFileList?> GetFileListAsync(string hash, CancellationToken ct = default);

    /// <summary>Stops a just-added torrent while the user chooses its files (a magnet link runs to get its file list).</summary>
    Task HoldForChoiceAsync(string hash, CancellationToken ct = default);

    /// <summary>
    /// Applies file choices to a just-added torrent, then starts it, or makes sure it is stopped when
    /// <paramref name="start"/> is false (a magnet link may still be running from fetching its file list).
    /// </summary>
    Task FinishAddAsync(string hash, FileChoices? choices, bool start, CancellationToken ct = default);

    /// <summary>Takes back a torrent the user stopped adding. Never deletes downloaded data.</summary>
    Task UndoAddAsync(string hash, CancellationToken ct = default);
}
