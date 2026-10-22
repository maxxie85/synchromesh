using Synchro.Core.Torrents;

namespace Synchro.AppServices.Torrents;

/// <summary>Control side of the feed. Only the connection service starts and stops it.</summary>
public interface ITorrentPoller : ITorrentFeed
{
    /// <summary>Raised once when polling starts failing. The argument is a human-readable reason.</summary>
    event EventHandler<string>? ConnectionLost;

    /// <summary>Raised when polling succeeds again after <see cref="ConnectionLost"/>.</summary>
    event EventHandler? ConnectionRestored;

    /// <summary>Starts polling the client, replacing any polling already running.</summary>
    void Start(ITransmissionClient client);

    /// <summary>
    /// Polls again right away with a full refresh, instead of waiting for the next tick. For changes that can touch
    /// any torrent (removing, queue order), because a delta only reports torrents that were recently active.
    /// </summary>
    void RequestRefresh();

    /// <summary>
    /// Polls again right away, re-reading just these torrents along with the usual delta: after an action on them.
    /// On a server with thousands of torrents that is a few KB instead of the whole list.
    /// </summary>
    void RequestRefresh(IReadOnlyCollection<string> hashes);

    Task StopAsync();
}
