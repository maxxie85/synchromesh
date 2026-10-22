using Synchro.Core.Torrents;

namespace Synchro.ViewModels.Formatting;

/// <summary>The words for a torrent's state, shared by the list and the details panel.</summary>
public static class StatusFormatter
{
    public static string Describe(TorrentStatus status, double percentDone, bool isStalled) => status switch
    {
        TorrentStatus.Stopped => percentDone >= 1 ? "Finished" : "Paused",
        TorrentStatus.QueuedToVerify => "Waiting to verify",
        TorrentStatus.Verifying => "Verifying",
        TorrentStatus.QueuedToDownload => "Queued",
        TorrentStatus.Downloading => isStalled ? "Stalled" : "Downloading",
        TorrentStatus.QueuedToSeed => "Queued to seed",
        TorrentStatus.Seeding => "Seeding",
        _ => status.ToString(),
    };
}
