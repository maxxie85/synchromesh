using Synchro.Core.Servers;

namespace Synchro.Core.Torrents;

/// <summary>What the app can ask a connected Transmission server, in domain terms (no JSON, no HTTP).</summary>
public interface ITransmissionClient : IDisposable
{
    /// <summary>What this server's RPC version allows (see <see cref="ServerCapabilities"/>).</summary>
    ServerCapabilities Capabilities { get; }

    /// <param name="recentlyActiveOnly">
    /// false: every torrent. true: only torrents active recently, plus ids removed since the last call.
    /// </param>
    Task<TorrentUpdate> GetTorrentsAsync(bool recentlyActiveOnly, CancellationToken ct = default);

    /// <summary>Just these torrents (by hash): after an action, instead of the whole list. Not a full refresh.</summary>
    Task<TorrentUpdate> GetTorrentsAsync(IReadOnlyCollection<string> hashes, CancellationToken ct = default);

    Task<SessionStats> GetSessionStatsAsync(CancellationToken ct = default);

    /// <summary>Details, trackers and peers of one torrent; null when the server no longer has it.</summary>
    /// <param name="files">How much of the file list to fetch (see <see cref="DetailFiles"/>).</param>
    Task<TorrentDetails?> GetTorrentDetailsAsync(string hash, DetailFiles files = DetailFiles.None, CancellationToken ct = default);

    /// <summary>The file list and how far the server got fetching it; null when the server doesn't have the torrent.</summary>
    Task<TorrentFileList?> GetFileListAsync(string hash, CancellationToken ct = default);

    /// <summary>Replaces the labels of the torrents with <paramref name="labels"/> (an empty list removes them all).</summary>
    Task SetLabelsAsync(IReadOnlyList<string> hashes, IReadOnlyList<string> labels, CancellationToken ct = default);

    /// <summary>Changes one torrent's settings (speed limits, priority, seeding limits, peer limit).</summary>
    /// <summary>
    /// Replaces a torrent's trackers. Each inner list is one tier (backups of each other), tiers in announce order.
    /// An empty list removes all trackers.
    /// </summary>
    Task SetTrackersAsync(string hash, IReadOnlyList<IReadOnlyList<string>> tiers, CancellationToken ct = default);

    /// <summary>
    /// Renames a file or folder in the torrent. <paramref name="path"/> is its path as the server lists it (the
    /// torrent's own name for the torrent itself); <paramref name="newName"/> replaces only the last part.
    /// </summary>
    Task RenamePathAsync(string hash, string path, string newName, CancellationToken ct = default);

    Task SetTorrentOptionsAsync(string hash, TorrentOptions options, CancellationToken ct = default);

    /// <summary>
    /// Points torrents at another folder on the server. With <paramref name="moveFiles"/> the server moves the data
    /// there; without, it only looks for the files there (for data the user moved themselves).
    /// </summary>
    Task SetLocationAsync(IReadOnlyList<string> hashes, string location, bool moveFiles, CancellationToken ct = default);

    /// <summary>Marks files to download or skip. Does nothing for an empty list (the protocol reads that as "all files").</summary>
    Task SetFilesWantedAsync(string hash, IReadOnlyList<int> fileIndices, bool wanted, CancellationToken ct = default);

    /// <summary>Sets file priority. Does nothing for an empty list (the protocol reads that as "all files").</summary>
    Task SetFilePriorityAsync(string hash, IReadOnlyList<int> fileIndices, FilePriority priority, CancellationToken ct = default);

    // Torrents are addressed by info-hash because numeric ids are not stable across daemon restarts.

    /// <param name="startNow">true skips the download/seed queue.</param>
    Task StartAsync(IReadOnlyList<string> hashes, bool startNow, CancellationToken ct = default);

    Task StopAsync(IReadOnlyList<string> hashes, CancellationToken ct = default);

    /// <summary>Re-checks the downloaded data against the torrent (after a disk problem, or files moved by hand).</summary>
    Task VerifyAsync(IReadOnlyList<string> hashes, CancellationToken ct = default);

    /// <summary>Asks the trackers for peers now instead of at the next scheduled announce.</summary>
    Task ReannounceAsync(IReadOnlyList<string> hashes, CancellationToken ct = default);

    Task RemoveAsync(IReadOnlyList<string> hashes, bool deleteLocalData, CancellationToken ct = default);

    Task MoveInQueueAsync(IReadOnlyList<string> hashes, QueueMove move, CancellationToken ct = default);

    Task<SpeedLimits> GetSpeedLimitsAsync(CancellationToken ct = default);

    Task SetSpeedLimitsAsync(SpeedLimits limits, CancellationToken ct = default);

    Task<AltSpeed> GetAltSpeedAsync(CancellationToken ct = default);

    /// <summary>Turns the alternative speed limits on or off, leaving everything else as it is.</summary>
    Task SetAltSpeedEnabledAsync(bool enabled, CancellationToken ct = default);

    /// <summary>Adds a torrent. A torrent the server already has is reported as a duplicate, not as an error.</summary>
    Task<AddTorrentResult> AddTorrentAsync(AddTorrentRequest request, CancellationToken ct = default);

    Task<Servers.ServerPreferences> GetServerPreferencesAsync(CancellationToken ct = default);

    Task SetServerPreferencesAsync(Servers.ServerPreferences preferences, CancellationToken ct = default);

    /// <summary>Asks Transmission's port test service whether the peer port is open, over "ipv4" or "ipv6".</summary>
    /// <summary>How many rules the server's blocklist has loaded.</summary>
    Task<int> GetBlocklistSizeAsync(CancellationToken ct = default);

    /// <summary>Has the server download its blocklist again; returns the new number of rules.</summary>
    Task<int> UpdateBlocklistAsync(CancellationToken ct = default);

    Task<bool> TestPortAsync(string ipProtocol, CancellationToken ct = default);

    /// <summary>The folder the server downloads to unless told otherwise.</summary>
    Task<string> GetDefaultDownloadDirectoryAsync(CancellationToken ct = default);

    /// <summary>Free space, in bytes, in a folder on the server.</summary>
    Task<long> GetFreeSpaceAsync(string path, CancellationToken ct = default);

    /// <summary>Free and total space of the disk a folder on the server is on.</summary>
    Task<StorageInfo> GetStorageAsync(string path, CancellationToken ct = default);
}
