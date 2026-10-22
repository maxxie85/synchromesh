using Synchro.Core.Torrents;

namespace Synchro.Tests;

internal static class TestData
{
    public static Torrent MakeTorrent(
        string hash,
        int id = 1,
        string? name = null,
        TorrentStatus status = TorrentStatus.Downloading,
        double done = 0.5,
        long added = 1_700_000_000,
        int error = 0,
        long eta = 60,
        double ratio = 0.25,
        bool stalled = false,
        int queue = 0,
        string[]? labels = null) =>
        new(id, hash, name ?? $"Torrent {hash}", status, done, 1_000_000_000, 2_000_000, 100_000, eta,
            ratio, error, error == 0 ? "" : "tracker said no", stalled, DateTimeOffset.FromUnixTimeSeconds(added), queue,
            labels ?? []);

    public static TorrentUpdate Full(params Torrent[] torrents) => new(torrents, [], IsFullRefresh: true);

    public static TorrentUpdate Delta(IEnumerable<Torrent> torrents, params int[] removed) =>
        new(torrents.ToList(), removed, IsFullRefresh: false);

    public static TorrentDetails MakeDetails(
        string hash,
        TorrentStatus status = TorrentStatus.Downloading,
        IReadOnlyList<TrackerInfo>? trackers = null,
        IReadOnlyList<PeerInfo>? peers = null) =>
        new(hash, $"Torrent {hash}", status, false, 0.5, 0, null, "/data", 1_000_000, 1_000_000, 500_000,
            500_000, 520_000, 100_000, 0, 0.2, 2_000_000, 100_000, 60, false, 0, true, 250, 4, 2, 1, 60,
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), null, null, null, null, null, 100, 10_000, false,
            $"magnet:?xt=urn:btih:{hash}", trackers ?? [], peers ?? []);

    public static TrackerInfo MakeTracker(
        int id,
        TrackerAnnounceState state = TrackerAnnounceState.Waiting,
        bool announced = true,
        bool succeeded = true,
        bool timedOut = false,
        string? result = null,
        bool backup = false,
        DateTimeOffset? next = null,
        int seeders = 10,
        int tier = 0) =>
        new(id, tier, $"udp://tracker{id}.example:6969/announce", $"tracker{id}.example", backup, state, announced,
            succeeded, timedOut, result, next, seeders, 2, 30, 5);

    public static PeerInfo MakePeer(string address, long down = 0, long up = 0) =>
        new(address, 51413, "Transmission 4.0.6", "TDE", 0.5, down, up, true, false, false);
}
