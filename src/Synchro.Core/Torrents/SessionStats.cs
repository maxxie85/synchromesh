namespace Synchro.Core.Torrents;

/// <param name="Current">Totals since the daemon started; null if the server didn't send them.</param>
/// <param name="Cumulative">Totals over the daemon's whole history; null if the server didn't send them.</param>
public sealed record SessionStats(
    int TorrentCount,
    int ActiveTorrentCount,
    int PausedTorrentCount,
    long DownloadSpeed,
    long UploadSpeed,
    StatsTotals? Current = null,
    StatsTotals? Cumulative = null);

/// <summary>Transfer totals (`current_stats` / `cumulative_stats`). Bytes and seconds.</summary>
public sealed record StatsTotals(long DownloadedBytes, long UploadedBytes, long FilesAdded, long SecondsActive, long SessionCount);
