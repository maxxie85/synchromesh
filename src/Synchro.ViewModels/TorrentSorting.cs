using Synchro.Core.Settings;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Formatting;

namespace Synchro.ViewModels;

/// <summary>
/// The order of the torrent list for each column. Ties fall back to the queue order, so the order is always the same
/// for the same data. A value the list shows as empty (an unknown ETA or ratio) sorts last in both directions.
/// </summary>
public static class TorrentSorting
{
    /// <summary>
    /// The direction a column starts with when clicked: biggest first for amounts (size, progress, speeds, ratio), so
    /// one click shows what you're most likely looking for; A to Z, soonest and queue order otherwise.
    /// </summary>
    public static bool StartsDescending(TorrentSortColumn column) => column is TorrentSortColumn.Size
        or TorrentSortColumn.Progress or TorrentSortColumn.Download or TorrentSortColumn.Upload or TorrentSortColumn.Ratio;

    /// <summary>The next sort after clicking <paramref name="column"/>: the other direction, or that column's start.</summary>
    public static TorrentSort Toggle(TorrentSort current, TorrentSortColumn column) =>
        current.Column == column
            ? current with { Descending = !current.Descending }
            : new TorrentSort(column, StartsDescending(column));

    public static Comparison<TorrentItemViewModel> Comparison(TorrentSort sort) => (a, b) =>
    {
        var byColumn = CompareColumn(sort, a.Torrent, b.Torrent);
        return byColumn != 0 ? byColumn : QueueOrder(a, b);
    };

    /// <summary>The server's queue, then name and hash so equal positions (briefly, while it reorders) stay stable.</summary>
    public static int QueueOrder(TorrentItemViewModel a, TorrentItemViewModel b)
    {
        var byQueue = a.Torrent.QueuePosition.CompareTo(b.Torrent.QueuePosition);
        if (byQueue != 0) return byQueue;

        var byName = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        return byName != 0 ? byName : string.CompareOrdinal(a.Hash, b.Hash);
    }

    private static int CompareColumn(TorrentSort sort, Torrent a, Torrent b)
    {
        int Directed(int result) => sort.Descending ? -result : result;

        // Unknown values last, whichever the direction; known values in the chosen direction.
        int KnownFirst(bool aKnown, bool bKnown, Func<int> compare) =>
            aKnown && bKnown ? Directed(compare()) : bKnown.CompareTo(aKnown);

        return sort.Column switch
        {
            TorrentSortColumn.Queue => Directed(a.QueuePosition.CompareTo(b.QueuePosition)),
            TorrentSortColumn.Name => Directed(NaturalStringComparer.Instance.Compare(a.Name, b.Name)),
            TorrentSortColumn.Size => Directed(a.TotalSize.CompareTo(b.TotalSize)),
            TorrentSortColumn.Progress => Directed(a.PercentDone.CompareTo(b.PercentDone)),
            TorrentSortColumn.Status => Directed(StatusRank(a).CompareTo(StatusRank(b))),
            TorrentSortColumn.Download => Directed(a.RateDownload.CompareTo(b.RateDownload)),
            TorrentSortColumn.Upload => Directed(a.RateUpload.CompareTo(b.RateUpload)),
            TorrentSortColumn.Eta => KnownFirst(HasEta(a), HasEta(b), () => a.Eta.CompareTo(b.Eta)),
            TorrentSortColumn.Ratio => KnownFirst(a.UploadRatio >= 0, b.UploadRatio >= 0,
                () => a.UploadRatio.CompareTo(b.UploadRatio)),
            _ => 0,
        };
    }

    // The list only shows an ETA while downloading.
    private static bool HasEta(Torrent t) => t.Status == TorrentStatus.Downloading && t.Eta >= 0;

    // Groups rows the way the Status column reads: errors first, then active work, then waiting, then stopped.
    private static int StatusRank(Torrent t) => t.HasError ? 0 : t.Status switch
    {
        TorrentStatus.Downloading => 1,
        TorrentStatus.Seeding => 2,
        TorrentStatus.Verifying => 3,
        TorrentStatus.QueuedToDownload => 4,
        TorrentStatus.QueuedToSeed => 5,
        TorrentStatus.QueuedToVerify => 6,
        TorrentStatus.Stopped when t.PercentDone < 1 => 7, // paused
        _ => 8,                                            // finished
    };
}
