using Synchro.Core.Torrents;

namespace Synchro.ViewModels.Services;

/// <summary>
/// Torrents handed to the app from outside (a .torrent opened in Finder, a magnet link clicked in a browser, a
/// command-line argument). They can arrive before the main window exists or while the first connection is still
/// being made, so they are held until the app is ready and then go through <see cref="IAddTorrentFlow"/>.
/// </summary>
public interface IExternalAddQueue
{
    /// <summary>Queues one batch of sources. Call on the UI thread. Empty batches are ignored.</summary>
    void Enqueue(IReadOnlyList<AddTorrentSource> sources);

    /// <summary>Called once start-up has finished (server setup, first connection attempt). Delivery begins here.</summary>
    void Start();
}
