using NSubstitute;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Notifications;
using Synchro.AppServices.Torrents;
using Synchro.Core.Notifications;
using Synchro.Core.Torrents;
using Xunit;
using static Synchro.Tests.TestData;

namespace Synchro.Tests;

public class TorrentNotificationsTests
{
    private readonly ITorrentFeed _feed = Substitute.For<ITorrentFeed>();
    private readonly IConnectionService _connection = Substitute.For<IConnectionService>();
    private readonly INotifier _notifier = Substitute.For<INotifier>();

    public TorrentNotificationsTests()
    {
        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connected, "4.1.3"));
        _ = new TorrentNotifications(_feed, _connection, _notifier);
    }

    private void Publish(TorrentUpdate update) =>
        _feed.TorrentsUpdated += Raise.Event<EventHandler<TorrentUpdate>>(_feed, update);

    private static Torrent Downloading(string hash, double done = 0.5, int id = 1) =>
        MakeTorrent(hash, id, name: $"Torrent {hash}", status: TorrentStatus.Downloading, done: done);

    private static Torrent Seeding(string hash, int id = 1) =>
        MakeTorrent(hash, id, name: $"Torrent {hash}", status: TorrentStatus.Seeding, done: 1);

    [Fact]
    public void Nothing_is_announced_for_the_first_full_refresh()
    {
        Publish(Full(Seeding("a"), MakeTorrent("b", 2, error: 3)));

        _notifier.DidNotReceiveWithAnyArgs().Notify(default!, default!);
    }

    [Fact]
    public void A_download_reaching_100_percent_is_announced_once()
    {
        Publish(Full(Downloading("a")));

        Publish(Delta([Seeding("a")]));
        Publish(Delta([Seeding("a")]));

        _notifier.Received(1).Notify("Download finished", "Torrent a");
    }

    [Fact]
    public void A_recheck_filling_up_is_not_a_finished_download()
    {
        Publish(Full(MakeTorrent("a", status: TorrentStatus.Verifying, done: 0.3)));

        Publish(Delta([MakeTorrent("a", status: TorrentStatus.Verifying, done: 1)]));
        Publish(Delta([Seeding("a")]));

        _notifier.DidNotReceiveWithAnyArgs().Notify(default!, default!);
    }

    [Fact]
    public void A_torrent_added_after_connecting_that_is_already_complete_is_not_announced()
    {
        Publish(Full(Downloading("a")));

        Publish(Delta([Seeding("new", id: 2)]));

        _notifier.DidNotReceiveWithAnyArgs().Notify(default!, default!);
    }

    [Fact]
    public void A_new_error_is_announced_with_the_servers_message()
    {
        Publish(Full(Downloading("a")));

        Publish(Delta([MakeTorrent("a", name: "Torrent a", error: 3)]));

        _notifier.Received(1).Notify("Torrent error", "Torrent a: tracker said no");
    }

    [Fact]
    public void Tracker_warnings_are_not_announced()
    {
        Publish(Full(Downloading("a")));

        Publish(Delta([MakeTorrent("a", error: 1)]));

        _notifier.DidNotReceiveWithAnyArgs().Notify(default!, default!);
    }

    [Fact]
    public void Many_downloads_finishing_together_become_one_notification()
    {
        var hashes = new[] { "a", "b", "c", "d", "e" };
        Publish(Full(hashes.Select((h, i) => Downloading(h, id: i + 1)).ToArray()));

        Publish(Delta(hashes.Select((h, i) => Seeding(h, id: i + 1))));

        _notifier.Received(1).Notify("5 downloads finished", "Torrent a, Torrent b, Torrent c and 2 more");
        _notifier.DidNotReceive().Notify("Download finished", Arg.Any<string>());
    }

    [Fact]
    public void Reconnecting_starts_from_a_new_baseline()
    {
        Publish(Full(Downloading("a")));

        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connecting));
        _connection.StatusChanged += Raise.Event();
        Publish(Delta([Seeding("a")]));      // ignored: waiting for the new baseline
        Publish(Full(Seeding("a")));          // the baseline itself is never announced

        _notifier.DidNotReceiveWithAnyArgs().Notify(default!, default!);
    }

    [Fact]
    public void A_removed_torrent_is_forgotten()
    {
        Publish(Full(Downloading("a", id: 7)));

        Publish(Delta([], 7));
        Publish(Delta([Seeding("a", id: 8)])); // same hash added again, already complete

        _notifier.DidNotReceiveWithAnyArgs().Notify(default!, default!);
    }
}
