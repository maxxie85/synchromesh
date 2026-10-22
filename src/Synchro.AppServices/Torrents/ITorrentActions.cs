using Synchro.Core.Torrents;

namespace Synchro.AppServices.Torrents;

/// <summary>Things the user can do to torrents. Each call refreshes the list afterwards.</summary>
public interface ITorrentActions
{
    Task StartAsync(IReadOnlyList<string> hashes, bool startNow = false, CancellationToken ct = default);
    Task StopAsync(IReadOnlyList<string> hashes, CancellationToken ct = default);
    Task VerifyAsync(IReadOnlyList<string> hashes, CancellationToken ct = default);
    Task ReannounceAsync(IReadOnlyList<string> hashes, CancellationToken ct = default);
    Task RemoveAsync(IReadOnlyList<string> hashes, bool deleteLocalData, CancellationToken ct = default);
    Task MoveInQueueAsync(IReadOnlyList<string> hashes, QueueMove move, CancellationToken ct = default);
    /// <summary>Gives each torrent its own new set of labels (hash → labels).</summary>
    Task SetLabelsAsync(IReadOnlyDictionary<string, IReadOnlyList<string>> labelsByHash, CancellationToken ct = default);

    /// <summary>Replaces the torrent's trackers; each inner list is one tier.</summary>
    Task SetTrackersAsync(string hash, IReadOnlyList<IReadOnlyList<string>> tiers, CancellationToken ct = default);

    /// <summary>Renames the torrent (<paramref name="path"/> = its name), or a file or folder in it.</summary>
    Task RenamePathAsync(string hash, string path, string newName, CancellationToken ct = default);

    Task SetOptionsAsync(string hash, TorrentOptions options, CancellationToken ct = default);
    Task MoveDataAsync(IReadOnlyList<string> hashes, string location, bool moveFiles, CancellationToken ct = default);

    /// <summary>The folder a torrent's data is in on the server; null when the server no longer has it.</summary>
    Task<string?> GetLocationAsync(string hash, CancellationToken ct = default);

    Task SetFilesWantedAsync(string hash, IReadOnlyList<int> fileIndices, bool wanted, CancellationToken ct = default);
    Task SetFilePriorityAsync(string hash, IReadOnlyList<int> fileIndices, FilePriority priority, CancellationToken ct = default);
}
