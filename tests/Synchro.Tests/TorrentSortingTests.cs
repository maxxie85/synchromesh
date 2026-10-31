using NSubstitute;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Settings;
using Synchro.Core.Torrents;
using Synchro.ViewModels;
using Xunit;
using static Synchro.Tests.TestData;

namespace Synchro.Tests;

public class TorrentSortingTests
{
    private readonly ITorrentFeed _feed = Substitute.For<ITorrentFeed>();
    private readonly IConnectionService _connection = Substitute.For<IConnectionService>();

    public TorrentSortingTests()
        => _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connected, "4.1.0"));

    private TorrentListViewModel Create() => new(_feed, _connection);

    private void Publish(TorrentUpdate update)
        => _feed.TorrentsUpdated += Raise.Event<EventHandler<TorrentUpdate>>(_feed, update);

    private static string[] Order(TorrentListViewModel vm) => vm.VisibleTorrents.Select(t => t.Hash).ToArray();

    [Fact]
    public void Starts_in_queue_order()
    {
        var vm = Create();

        Publish(Full(MakeTorrent("b", 1, queue: 1), MakeTorrent("a", 2, queue: 0)));

        Assert.Equal(TorrentSort.Default, vm.Sort);
        Assert.Equal(["a", "b"], Order(vm));
    }

    [Fact]
    public void Clicking_a_column_sorts_by_it_and_clicking_again_reverses()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("x", 1, name: "Torrent 10", queue: 0), MakeTorrent("y", 2, name: "torrent 9", queue: 1),
            MakeTorrent("z", 3, name: "Alpha", queue: 2)));

        vm.SortByCommand.Execute(TorrentSortColumn.Name);
        Assert.Equal(["z", "y", "x"], Order(vm)); // natural order: 9 before 10, any case

        vm.SortByCommand.Execute(TorrentSortColumn.Name);
        Assert.Equal(new TorrentSort(TorrentSortColumn.Name, Descending: true), vm.Sort);
        Assert.Equal(["x", "y", "z"], Order(vm));
    }

    [Theory]
    [InlineData(TorrentSortColumn.Size, true)]
    [InlineData(TorrentSortColumn.Progress, true)]
    [InlineData(TorrentSortColumn.Download, true)]
    [InlineData(TorrentSortColumn.Upload, true)]
    [InlineData(TorrentSortColumn.Ratio, true)]
    [InlineData(TorrentSortColumn.Name, false)]
    [InlineData(TorrentSortColumn.Status, false)]
    [InlineData(TorrentSortColumn.Eta, false)]
    [InlineData(TorrentSortColumn.Queue, false)]
    public void Amounts_start_biggest_first(TorrentSortColumn column, bool descending)
    {
        Assert.Equal(descending, TorrentSorting.StartsDescending(column));
        var other = column == TorrentSortColumn.Name ? TorrentSortColumn.Size : TorrentSortColumn.Name;
        Assert.Equal(new TorrentSort(column, descending), TorrentSorting.Toggle(new TorrentSort(other, false), column));
    }

    [Fact]
    public void Sorts_by_each_value()
    {
        var vm = Create();
        Publish(Full(
            MakeTorrent("small", 1, queue: 0, done: 0.9, ratio: 2) with { TotalSize = 10, RateDownload = 5, RateUpload = 300 },
            MakeTorrent("big", 2, queue: 1, done: 0.1, ratio: 0.5) with { TotalSize = 1000, RateDownload = 50, RateUpload = 3 }));

        vm.Sort = new TorrentSort(TorrentSortColumn.Size, Descending: true);
        Assert.Equal(["big", "small"], Order(vm));
        vm.Sort = new TorrentSort(TorrentSortColumn.Progress, Descending: true);
        Assert.Equal(["small", "big"], Order(vm));
        vm.Sort = new TorrentSort(TorrentSortColumn.Download, Descending: true);
        Assert.Equal(["big", "small"], Order(vm));
        vm.Sort = new TorrentSort(TorrentSortColumn.Upload, Descending: true);
        Assert.Equal(["small", "big"], Order(vm));
        vm.Sort = new TorrentSort(TorrentSortColumn.Ratio, Descending: false);
        Assert.Equal(["big", "small"], Order(vm));
        vm.Sort = new TorrentSort(TorrentSortColumn.Queue, Descending: true);
        Assert.Equal(["big", "small"], Order(vm));
    }

    [Fact]
    public void Status_groups_errors_then_active_then_waiting_then_stopped()
    {
        var vm = Create();
        Publish(Full(
            MakeTorrent("finished", 1, queue: 0, status: TorrentStatus.Stopped, done: 1),
            MakeTorrent("paused", 2, queue: 1, status: TorrentStatus.Stopped, done: 0.5),
            MakeTorrent("queued", 3, queue: 2, status: TorrentStatus.QueuedToDownload),
            MakeTorrent("seeding", 4, queue: 3, status: TorrentStatus.Seeding, done: 1),
            MakeTorrent("downloading", 5, queue: 4, status: TorrentStatus.Downloading),
            MakeTorrent("error", 6, queue: 5, status: TorrentStatus.Stopped, error: 2)));

        vm.SortByCommand.Execute(TorrentSortColumn.Status);

        Assert.Equal(["error", "downloading", "seeding", "queued", "paused", "finished"], Order(vm));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_unknown_eta_or_ratio_is_last_in_both_directions(bool descending)
    {
        var vm = Create();
        Publish(Full(
            MakeTorrent("unknown", 1, queue: 0, eta: -1, ratio: -1),
            MakeTorrent("seeding", 2, queue: 1, status: TorrentStatus.Seeding, eta: 5), // no ETA shown while seeding
            MakeTorrent("soon", 3, queue: 2, eta: 10, ratio: 0.1),
            MakeTorrent("later", 4, queue: 3, eta: 1000, ratio: 3)));

        vm.Sort = new TorrentSort(TorrentSortColumn.Eta, descending);
        Assert.Equal(descending ? ["later", "soon"] : ["soon", "later"], Order(vm)[..2]);
        Assert.Equal(["unknown", "seeding"], Order(vm)[2..]); // ties keep queue order

        vm.Sort = new TorrentSort(TorrentSortColumn.Ratio, descending);
        Assert.Equal("unknown", Order(vm)[^1]);
    }

    [Fact]
    public void Rows_move_when_their_sort_value_changes_and_keep_their_identity()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("a", 1, queue: 0) with { RateDownload = 100 }, MakeTorrent("b", 2, queue: 1) with { RateDownload = 10 }));
        vm.SortByCommand.Execute(TorrentSortColumn.Download);
        var rowA = vm.VisibleTorrents[0];
        var restores = 0;
        vm.SelectionRestoreRequested += (_, _) => restores++;

        Publish(Delta([MakeTorrent("b", 2, queue: 1) with { RateDownload = 500 }]));

        Assert.Equal(["b", "a"], Order(vm));
        Assert.Same(rowA, vm.VisibleTorrents[1]);
        Assert.Equal(1, restores); // the view puts the selection back after the move
    }

    [Fact]
    public void New_rows_arrive_in_their_sorted_place()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("b", 1, name: "Bravo", queue: 0), MakeTorrent("d", 2, name: "Delta", queue: 1)));
        vm.SortByCommand.Execute(TorrentSortColumn.Name);

        Publish(Delta([MakeTorrent("c", 3, name: "Charlie", queue: 2)]));

        Assert.Equal(["b", "c", "d"], Order(vm));
    }

    [Fact]
    public void The_selection_survives_a_sort()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("a", 1, name: "Zulu", queue: 0), MakeTorrent("b", 2, name: "Alpha", queue: 1)));
        vm.SetSelection([vm.VisibleTorrents[0]]);

        vm.SortByCommand.Execute(TorrentSortColumn.Name);

        Assert.Equal(["b", "a"], Order(vm));
        Assert.True(vm.IsSelected("a"));
        Assert.Equal("a", Assert.Single(vm.SelectedTorrents).Hash);
    }

    [Fact]
    public void Reordering_never_uses_move_which_the_grid_ignores()
    {
        var vm = Create();
        var torrents = Enumerable.Range(0, 40)
            .Select(i => MakeTorrent($"h{i:D2}", i + 1, name: $"Torrent {i:D2}", queue: i) with { RateDownload = i }).ToArray();
        Publish(Full(torrents));
        var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        vm.VisibleTorrents.CollectionChanged += (_, e) => actions.Add(e.Action);

        // A new sort turns the whole list around: one reset.
        vm.SortByCommand.Execute(TorrentSortColumn.Download);
        Assert.Equal([System.Collections.Specialized.NotifyCollectionChangedAction.Reset], actions);
        Assert.Equal("h39", vm.VisibleTorrents[0].Hash);

        // A poll where one row overtakes the others: that row is removed and inserted again.
        actions.Clear();
        Publish(Delta([torrents[0] with { RateDownload = 1000 }]));
        Assert.Equal("h00", vm.VisibleTorrents[0].Hash);
        Assert.Equal([System.Collections.Specialized.NotifyCollectionChangedAction.Remove,
            System.Collections.Specialized.NotifyCollectionChangedAction.Add], actions);
    }
}
