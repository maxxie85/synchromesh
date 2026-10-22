using Synchro.Core.Torrents;

namespace Synchro.AppServices.Torrents;

/// <summary>Read-only stream of server data for view models. They can listen, but not start or stop polling.</summary>
public interface ITorrentFeed
{
    event EventHandler<TorrentUpdate>? TorrentsUpdated;
    event EventHandler<SessionStats>? StatsUpdated;
}
