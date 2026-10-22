using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Notifications;
using Synchro.Core.Torrents;

namespace Synchro.AppServices.Notifications;

/// <summary>
/// Watches the torrent feed and notifies when a download finishes or a torrent runs into an error.
/// The first full refresh after (re)connecting is only a baseline, so nothing that was already done is announced.
/// </summary>
public sealed class TorrentNotifications : IDisposable
{
    // Tracker warnings (1) come and go all the time; tracker errors (2) and local errors (3) need attention.
    private const int FirstNotableError = 2;
    private const int GroupAbove = 3;

    private readonly ITorrentFeed _feed;
    private readonly IConnectionService _connection;
    private readonly INotifier _notifier;
    private readonly Dictionary<string, Torrent> _known = [];
    private readonly Dictionary<int, string> _hashById = [];
    private bool _needBaseline = true;

    public TorrentNotifications(ITorrentFeed feed, IConnectionService connection, INotifier notifier)
    {
        _feed = feed;
        _connection = connection;
        _notifier = notifier;

        _feed.TorrentsUpdated += OnTorrentsUpdated;
        _connection.StatusChanged += OnConnectionStatusChanged;
    }

    public void Dispose()
    {
        _feed.TorrentsUpdated -= OnTorrentsUpdated;
        _connection.StatusChanged -= OnConnectionStatusChanged;
    }

    // A new connection (or another server) starts from a fresh baseline.
    private void OnConnectionStatusChanged(object? sender, EventArgs e)
    {
        if (_connection.Status.State != ConnectionState.Connecting) return;

        _known.Clear();
        _hashById.Clear();
        _needBaseline = true;
    }

    private void OnTorrentsUpdated(object? sender, TorrentUpdate update)
    {
        if (_needBaseline)
        {
            if (!update.IsFullRefresh) return;

            Remember(update.Torrents);
            _needBaseline = false;
            return;
        }

        Forget(update);

        var finished = new List<Torrent>();
        foreach (var t in update.Torrents)
        {
            if (_known.TryGetValue(t.HashString, out var before))
            {
                if (HasFinished(before, t)) finished.Add(t);
                if (before.Error < FirstNotableError && t.Error >= FirstNotableError)
                    _notifier.Notify("Torrent error", $"{t.Name}: {t.ErrorString}");
            }
        }

        Remember(update.Torrents);
        AnnounceFinished(finished);
    }

    // Progress crossing 100% while verifying is a re-check of data that was already there, not a finished download.
    private static bool HasFinished(Torrent before, Torrent now) =>
        before.PercentDone < 1 && now.PercentDone >= 1 && !IsVerifying(before) && !IsVerifying(now);

    private static bool IsVerifying(Torrent t) => t.Status is TorrentStatus.Verifying or TorrentStatus.QueuedToVerify;

    private void AnnounceFinished(List<Torrent> finished)
    {
        if (finished.Count == 0) return;

        if (finished.Count > GroupAbove)
        {
            var names = string.Join(", ", finished.Take(GroupAbove).Select(t => t.Name));
            _notifier.Notify($"{finished.Count} downloads finished", $"{names} and {finished.Count - GroupAbove} more");
            return;
        }

        foreach (var t in finished)
            _notifier.Notify("Download finished", t.Name);
    }

    private void Remember(IEnumerable<Torrent> torrents)
    {
        foreach (var t in torrents)
        {
            _known[t.HashString] = t;
            _hashById[t.Id] = t.HashString;
        }
    }

    private void Forget(TorrentUpdate update)
    {
        foreach (var id in update.RemovedIds)
        {
            if (_hashById.Remove(id, out var hash))
                _known.Remove(hash);
        }

        // A full refresh lists everything, so anything missing is gone.
        if (update.IsFullRefresh)
        {
            var present = update.Torrents.Select(t => t.HashString).ToHashSet();
            foreach (var hash in _known.Keys.Where(h => !present.Contains(h)).ToList())
                _known.Remove(hash);
        }
    }
}
