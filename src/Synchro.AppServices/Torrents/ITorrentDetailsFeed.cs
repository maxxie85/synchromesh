using Synchro.Core.Torrents;

namespace Synchro.AppServices.Torrents;

/// <summary>Details of the one torrent being watched, refreshed on every poll alongside the list.</summary>
/// <param name="Hash">The torrent that was asked for.</param>
/// <param name="Details">Null when the server no longer has it.</param>
public sealed record TorrentDetailsUpdate(string Hash, TorrentDetails? Details);

public interface ITorrentDetailsFeed
{
    /// <summary>Raised on the UI thread, like the other feed events.</summary>
    event EventHandler<TorrentDetailsUpdate>? DetailsUpdated;

    /// <summary>
    /// Starts fetching details for <paramref name="hash"/> on each poll, beginning right away; null stops.
    /// Updates for a previously watched torrent may still arrive once, so check <see cref="TorrentDetailsUpdate.Hash"/>.
    /// </summary>
    /// <param name="includeFiles">
    /// Also fetch the file list (only while the Files tab is showing: it can be large). The names are fetched once;
    /// later polls fetch only progress and merge it in, until the torrent, the number of files or an action changes them.
    /// </param>
    void Watch(string? hash, bool includeFiles = false);
}
