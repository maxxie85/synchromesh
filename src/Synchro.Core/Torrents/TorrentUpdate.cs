namespace Synchro.Core.Torrents;

/// <summary>
/// Result of one poll. A full refresh lists every torrent on the server, so anything not in it is gone.
/// A delta lists only recently active torrents plus the ids of torrents removed since the last poll.
/// </summary>
public sealed record TorrentUpdate(
    IReadOnlyList<Torrent> Torrents,
    IReadOnlyList<int> RemovedIds,
    bool IsFullRefresh);
