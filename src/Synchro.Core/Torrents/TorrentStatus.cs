namespace Synchro.Core.Torrents;

/// <summary>Values match the `status` field of torrent_get (spec 3.3).</summary>
public enum TorrentStatus
{
    Stopped = 0,
    QueuedToVerify = 1,
    Verifying = 2,
    QueuedToDownload = 3,
    Downloading = 4,
    QueuedToSeed = 5,
    Seeding = 6,
}
