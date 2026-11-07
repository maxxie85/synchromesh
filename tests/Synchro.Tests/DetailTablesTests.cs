using System.Collections.Specialized;
using NSubstitute;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Settings;
using Synchro.Core.Torrents;
using Synchro.ViewModels;
using Synchro.ViewModels.Details;
using Synchro.ViewModels.Services;
using Xunit;
using static Synchro.Tests.TestData;

namespace Synchro.Tests;

public class RowOrderTests
{
    private sealed record Row(string Name);

    private static BulkObservableCollection<Row> Rows(params Row[] rows)
    {
        var collection = new BulkObservableCollection<Row>();
        collection.ReplaceAll(rows);
        return collection;
    }

    [Fact]
    public void Adds_drops_and_moves_rows_without_move_notifications()
    {
        Row a = new("a"), b = new("b"), c = new("c"), d = new("d");
        var rows = Rows(a, b, c);
        var actions = new List<NotifyCollectionChangedAction>();
        rows.CollectionChanged += (_, e) => actions.Add(e.Action);

        Assert.True(RowOrder.Apply(rows, [c, a, d]));

        Assert.Equal([c, a, d], rows);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Move, actions);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }

    [Fact]
    public void Nothing_changes_when_the_order_is_already_right()
    {
        Row a = new("a"), b = new("b");
        var rows = Rows(a, b);
        var changed = 0;
        rows.CollectionChanged += (_, _) => changed++;

        Assert.False(RowOrder.Apply(rows, [a, b]));
        Assert.Equal(0, changed);
    }

    [Fact]
    public void Many_changes_are_one_reset()
    {
        var all = Enumerable.Range(0, 40).Select(i => new Row($"r{i}")).ToArray();
        var rows = Rows(all);
        var actions = new List<NotifyCollectionChangedAction>();
        rows.CollectionChanged += (_, e) => actions.Add(e.Action);

        RowOrder.Apply(rows, all.Reverse().ToArray());

        Assert.Equal([NotifyCollectionChangedAction.Reset], actions);
        Assert.Equal(all.Reverse(), rows);
    }
}

public class TrackerAndPeerSortingTests
{
    private readonly ITorrentFeed _feed = Substitute.For<ITorrentFeed>();
    private readonly ITorrentDetailsFeed _details = Substitute.For<ITorrentDetailsFeed>();
    private readonly IConnectionService _connection = Substitute.For<IConnectionService>();
    private readonly ITorrentActions _actions = Substitute.For<ITorrentActions>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly TorrentListViewModel _list;

    public TrackerAndPeerSortingTests()
    {
        _dialogs.ConfirmAsync(default!, default!, default!, default).ReturnsForAnyArgs(true);
        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connected, "4.1.3"));
        _list = new TorrentListViewModel(_feed, _connection);
        _feed.TorrentsUpdated += Raise.Event<EventHandler<TorrentUpdate>>(_feed, Full(MakeTorrent("a", 1)));
    }

    private TorrentDetailsViewModel Create()
    {
        var vm = new TorrentDetailsViewModel(_details, _list, TimeProvider.System, _actions, _dialogs);
        _list.SetSelection([]);
        _list.SetSelection(_list.VisibleTorrents);
        return vm;
    }

    private void Publish(IReadOnlyList<TrackerInfo>? trackers = null, IReadOnlyList<PeerInfo>? peers = null) =>
        _details.DetailsUpdated += Raise.Event<EventHandler<TorrentDetailsUpdate>>(_details,
            new TorrentDetailsUpdate("a", MakeDetails("a", trackers: trackers, peers: peers)));

    [Fact]
    public void Peers_start_fastest_first_and_follow_the_speeds()
    {
        var vm = Create();

        Publish(peers: [MakePeer("10.0.0.1", down: 10), MakePeer("10.0.0.2", down: 500), MakePeer("10.0.0.3", down: 90)]);
        Assert.Equal(["10.0.0.2", "10.0.0.3", "10.0.0.1"], vm.Peers.Select(p => p.Address));

        var first = vm.Peers[0];
        Publish(peers: [MakePeer("10.0.0.1", down: 900), MakePeer("10.0.0.2", down: 500), MakePeer("10.0.0.3", down: 90)]);
        Assert.Equal(["10.0.0.1", "10.0.0.2", "10.0.0.3"], vm.Peers.Select(p => p.Address));
        Assert.Same(first, vm.Peers[1]); // rows are kept, only moved
    }

    [Fact]
    public void Clicking_a_peer_column_sorts_by_it_and_again_reverses()
    {
        var vm = Create();
        Publish(peers: [MakePeer("10.0.0.10"), MakePeer("10.0.0.9"), MakePeer("10.0.0.2")]);

        vm.SortPeersCommand.Execute(PeerItemViewModel.Columns.Address);
        Assert.Equal(["10.0.0.2", "10.0.0.9", "10.0.0.10"], vm.Peers.Select(p => p.Address)); // natural order

        vm.SortPeersCommand.Execute(PeerItemViewModel.Columns.Address);
        Assert.Equal(new TableSort(PeerItemViewModel.Columns.Address, true), vm.PeerSort);
        Assert.Equal(["10.0.0.10", "10.0.0.9", "10.0.0.2"], vm.Peers.Select(p => p.Address));
    }

    [Fact]
    public void Trackers_sort_by_tier_until_a_column_is_clicked_and_unknown_counts_stay_last()
    {
        var vm = Create();
        Publish(trackers: [MakeTracker(1, seeders: 5, tier: 1), MakeTracker(2, seeders: -1, tier: 0), MakeTracker(3, seeders: 40, tier: 0)]);
        Assert.Equal([2, 3, 1], vm.Trackers.Select(t => t.Id));

        vm.SortTrackersCommand.Execute(TrackerItemViewModel.Columns.Seeders);
        Assert.Equal([3, 1, 2], vm.Trackers.Select(t => t.Id)); // most seeders first, unknown last

        vm.SortTrackersCommand.Execute(TrackerItemViewModel.Columns.Seeders);
        Assert.Equal([1, 3, 2], vm.Trackers.Select(t => t.Id)); // unknown still last
    }

    [Fact]
    public async Task Editing_trackers_sends_the_servers_order_whatever_the_table_shows()
    {
        var vm = Create();
        Publish(trackers: [MakeTracker(1, seeders: 1), MakeTracker(2, seeders: 50), MakeTracker(3, seeders: 9, tier: 1)]);
        vm.SortTrackersCommand.Execute(TrackerItemViewModel.Columns.Seeders);

        await vm.RemoveTrackerCommand.ExecuteAsync(vm.Trackers.Single(t => t.Id == 3));

        await _actions.Received(1).SetTrackersAsync("a", Arg.Is<IReadOnlyList<IReadOnlyList<string>>>(tiers =>
            tiers.Count == 1 && tiers[0].SequenceEqual(new[]
                { "udp://tracker1.example:6969/announce", "udp://tracker2.example:6969/announce" })));
    }
}

public class FileSortingTests
{
    private static TorrentFile MakeFile(int index, string path, long length, long done = 0,
        FilePriority priority = FilePriority.Normal) => new(index, path, length, done, true, priority);

    // Root folder "T" holds big.iso, small.txt and a Docs folder with two files.
    private static TorrentFile[] Files(long docsDone = 0) =>
    [
        MakeFile(0, "T/small.txt", 10, 10, FilePriority.Low),
        MakeFile(1, "T/big.iso", 1000, 100, FilePriority.High),
        MakeFile(2, "T/Docs/b.pdf", 300, docsDone),
        MakeFile(3, "T/Docs/a.pdf", 50),
    ];

    private static FilesViewModel Create()
    {
        var vm = new FilesViewModel(Substitute.For<ITorrentActions>(), Substitute.For<IDialogService>());
        vm.Apply("h", Files());
        return vm;
    }

    private static string[] Names(FilesViewModel vm) => vm.Rows.Select(r => r.Name).ToArray();

    [Fact]
    public void Folders_come_first_and_each_folder_is_sorted_on_its_own()
    {
        var vm = Create();
        vm.Rows.Single(r => r.Name == "Docs").IsExpanded = true;
        Assert.Equal(["Docs", "a.pdf", "b.pdf", "big.iso", "small.txt"], Names(vm));

        vm.SortByCommand.Execute(FilesViewModel.Columns.Size);

        Assert.Equal(new TableSort(FilesViewModel.Columns.Size, true), vm.Sort); // biggest first
        Assert.Equal(["Docs", "b.pdf", "a.pdf", "big.iso", "small.txt"], Names(vm));
    }

    [Fact]
    public void A_sort_by_progress_follows_the_downloads()
    {
        var vm = Create();
        vm.Rows.Single(r => r.Name == "Docs").IsExpanded = true;
        vm.Sort = new TableSort(FilesViewModel.Columns.Done, true);
        Assert.Equal(["Docs", "a.pdf", "b.pdf", "big.iso", "small.txt"], Names(vm)); // ties by name

        vm.Apply("h", Files(docsDone: 200));

        Assert.Equal(["Docs", "b.pdf", "a.pdf", "big.iso", "small.txt"], Names(vm));
    }

    [Fact]
    public void Priority_sorts_high_first_and_a_mixed_folder_last()
    {
        var vm = Create();

        vm.Sort = new TableSort(FilesViewModel.Columns.Priority, true);

        Assert.Equal(["Docs", "big.iso", "small.txt"], Names(vm)); // folders still first
    }
}
