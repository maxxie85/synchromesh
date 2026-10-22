using NSubstitute;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.ViewModels;
using Xunit;
using static Synchro.Tests.TestData;

namespace Synchro.Tests;

public class TorrentListViewModelTests
{
    private readonly ITorrentFeed _feed = Substitute.For<ITorrentFeed>();
    private readonly IConnectionService _connection = Substitute.For<IConnectionService>();

    public TorrentListViewModelTests()
        => _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connected, "4.1.0"));

    private TorrentListViewModel Create() => new(_feed, _connection);

    private void Publish(TorrentUpdate update)
        => _feed.TorrentsUpdated += Raise.Event<EventHandler<TorrentUpdate>>(_feed, update);

    [Fact]
    public void A_full_refresh_lists_torrents_in_queue_order()
    {
        var vm = Create();

        Publish(Full(MakeTorrent("third", 1, queue: 2), MakeTorrent("first", 2, queue: 0), MakeTorrent("second", 3, queue: 1)));

        Assert.Equal(new[] { "first", "second", "third" }, vm.VisibleTorrents.Select(t => t.Hash));
    }

    [Fact]
    public void A_delta_updates_the_existing_row_in_place()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("a", 1, done: 0.2)));
        var row = vm.VisibleTorrents.Single();

        Publish(Delta([MakeTorrent("a", 1, done: 0.8)]));

        Assert.Same(row, vm.VisibleTorrents.Single());
        Assert.Equal(80, row.Progress);
    }

    [Fact]
    public void A_delta_adds_new_torrents()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("a", 1, queue: 0)));

        Publish(Delta([MakeTorrent("b", 2, queue: 1)]));

        Assert.Equal(new[] { "a", "b" }, vm.VisibleTorrents.Select(t => t.Hash));
    }

    [Fact]
    public void Removed_ids_remove_rows()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("a", 1), MakeTorrent("b", 2)));

        Publish(Delta([], 1));

        Assert.Equal("b", Assert.Single(vm.VisibleTorrents).Hash);
    }

    [Fact]
    public void A_full_refresh_drops_torrents_the_server_no_longer_has()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("a", 1), MakeTorrent("b", 2)));

        Publish(Full(MakeTorrent("b", 2)));

        Assert.Equal("b", Assert.Single(vm.VisibleTorrents).Hash);
    }

    [Fact]
    public void Filters_show_counts_and_narrow_the_list()
    {
        var vm = Create();
        Publish(Full(
            MakeTorrent("d1", 1, status: TorrentStatus.Downloading),
            MakeTorrent("d2", 2, status: TorrentStatus.QueuedToDownload),
            MakeTorrent("s1", 3, status: TorrentStatus.Seeding),
            MakeTorrent("p1", 4, status: TorrentStatus.Stopped, done: 0.3),
            MakeTorrent("e1", 5, status: TorrentStatus.Stopped, error: 3)));

        int Count(TorrentFilter f) => vm.Filters.Single(x => x.Filter == f).Count;
        Assert.Equal(5, Count(TorrentFilter.All));
        Assert.Equal(2, Count(TorrentFilter.Downloading));
        Assert.Equal(1, Count(TorrentFilter.Seeding));
        Assert.Equal(2, Count(TorrentFilter.Paused));
        Assert.Equal(1, Count(TorrentFilter.Errors));

        vm.SelectedFilter = vm.Filters.Single(f => f.Filter == TorrentFilter.Seeding);
        Assert.Equal("s1", Assert.Single(vm.VisibleTorrents).Hash);
    }

    [Fact]
    public void A_row_leaves_the_filtered_view_when_its_status_changes()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("a", 1, status: TorrentStatus.Downloading)));
        vm.SelectedFilter = vm.Filters.Single(f => f.Filter == TorrentFilter.Downloading);
        Assert.Single(vm.VisibleTorrents);

        Publish(Delta([MakeTorrent("a", 1, status: TorrentStatus.Seeding, done: 1)]));

        Assert.Empty(vm.VisibleTorrents);
        Assert.Equal("No torrents match this filter.", vm.EmptyMessage);
    }

    [Fact]
    public void Search_matches_names_ignoring_case()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("a", 1, name: "Ubuntu 26.04"), MakeTorrent("b", 2, name: "Fedora 44")));

        vm.SearchText = "ubUNTU";

        Assert.Equal("a", Assert.Single(vm.VisibleTorrents).Hash);
    }

    [Fact]
    public void A_new_connection_attempt_clears_the_list_but_a_transient_drop_does_not()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("a", 1)));

        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Failed, Message: "Connection lost"));
        _connection.StatusChanged += Raise.Event<EventHandler>(_connection, EventArgs.Empty);
        Assert.Single(vm.VisibleTorrents);

        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connecting));
        _connection.StatusChanged += Raise.Event<EventHandler>(_connection, EventArgs.Empty);
        Assert.Empty(vm.VisibleTorrents);
    }

    [Fact]
    public void Shows_a_message_when_the_server_has_no_torrents()
    {
        var vm = Create();

        Publish(Full());

        Assert.Equal("There are no torrents on this server yet.", vm.EmptyMessage);
        Assert.True(vm.HasEmptyMessage);
    }

    [Fact]
    public void Rows_are_re_sorted_when_queue_positions_change()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("a", 1, queue: 0), MakeTorrent("b", 2, queue: 1), MakeTorrent("c", 3, queue: 2)));

        // "c" moved to the top; the others shifted down.
        Publish(Delta([MakeTorrent("a", 1, queue: 1), MakeTorrent("b", 2, queue: 2), MakeTorrent("c", 3, queue: 0)]));

        Assert.Equal(new[] { "c", "a", "b" }, vm.VisibleTorrents.Select(t => t.Hash));
    }

    [Fact]
    public void Selection_is_kept_by_hash_when_rows_are_reordered()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("a", 1, queue: 0), MakeTorrent("b", 2, queue: 1), MakeTorrent("c", 3, queue: 2)));
        vm.SetSelection(vm.VisibleTorrents.Where(t => t.Hash == "c"));
        var restores = 0;
        vm.SelectionRestoreRequested += (_, _) => restores++;

        Publish(Delta([MakeTorrent("a", 1, queue: 1), MakeTorrent("b", 2, queue: 2), MakeTorrent("c", 3, queue: 0)]));

        Assert.True(vm.IsSelected("c"));
        Assert.False(vm.IsSelected("a"));
        Assert.Equal("c", Assert.Single(vm.SelectedTorrents).Hash);
        Assert.Equal(1, restores); // the view is asked once to put the selection back on the moved row
    }

    [Fact]
    public void Selection_reports_from_the_view_are_ignored_while_the_list_is_being_rebuilt()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("a", 1, queue: 0), MakeTorrent("b", 2, queue: 1)));
        vm.SetSelection(vm.VisibleTorrents);
        // While rows are removed and re-added, the real list control reports a shrunken selection.
        vm.VisibleTorrents.CollectionChanged += (_, _) => vm.SetSelection([]);

        Publish(Delta([MakeTorrent("a", 1, queue: 1), MakeTorrent("b", 2, queue: 0)]));

        Assert.True(vm.IsSelected("a"));
        Assert.True(vm.IsSelected("b"));
    }

    [Fact]
    public void A_removed_torrent_leaves_the_selection_and_raises_selection_changed()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("a", 1), MakeTorrent("b", 2)));
        vm.SetSelection(vm.VisibleTorrents);
        var changed = 0;
        vm.SelectionChanged += (_, _) => changed++;

        Publish(Delta([], 1));

        Assert.Equal("b", Assert.Single(vm.SelectedTorrents).Hash);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Selected_rows_that_a_filter_hides_are_no_longer_selected()
    {
        var vm = Create();
        Publish(Full(
            MakeTorrent("d", 1, status: TorrentStatus.Downloading),
            MakeTorrent("s", 2, status: TorrentStatus.Seeding, done: 1)));
        vm.SetSelection(vm.VisibleTorrents);

        vm.SelectedFilter = vm.Filters.Single(f => f.Filter == TorrentFilter.Seeding);

        Assert.Equal("s", Assert.Single(vm.SelectedTorrents).Hash);
        Assert.False(vm.IsSelected("d"));
    }

    [Fact]
    public void SetSelection_raises_selection_changed_only_when_the_selection_differs()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("a", 1), MakeTorrent("b", 2)));
        var changed = 0;
        vm.SelectionChanged += (_, _) => changed++;

        vm.SetSelection(vm.VisibleTorrents.Skip(1)); // the first load already selected the top row
        vm.SetSelection(vm.VisibleTorrents.Skip(1));

        Assert.Equal(1, changed);
    }

    [Fact]
    public void A_new_connection_clears_the_selection()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("a", 1)));
        vm.SetSelection(vm.VisibleTorrents);

        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connecting));
        _connection.StatusChanged += Raise.Event<EventHandler>(_connection, EventArgs.Empty);

        Assert.Empty(vm.SelectedTorrents);
    }

    [Fact]
    public void Only_the_active_filter_is_marked_selected()
    {
        var vm = Create();

        Assert.True(vm.Filters.Single(f => f.Filter == TorrentFilter.All).IsSelected);

        vm.SelectedFilter = vm.Filters.Single(f => f.Filter == TorrentFilter.Errors);

        Assert.Equal(new[] { TorrentFilter.Errors }, vm.Filters.Where(f => f.IsSelected).Select(f => f.Filter));
    }

    [Fact]
    public void The_select_filter_command_switches_the_filter_and_narrows_the_list()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("down", 1, status: TorrentStatus.Downloading),
            MakeTorrent("seed", 2, status: TorrentStatus.Seeding, done: 1)));

        vm.SelectFilterCommand.Execute(TorrentFilter.Seeding);

        Assert.Equal(TorrentFilter.Seeding, vm.SelectedFilter.Filter);
        Assert.Equal(new[] { "seed" }, vm.VisibleTorrents.Select(t => t.Hash));
    }

    [Fact]
    public void The_first_load_selects_the_top_row_and_tells_the_view()
    {
        var vm = Create();
        var restored = 0;
        var changed = 0;
        vm.SelectionRestoreRequested += (_, _) => restored++;
        vm.SelectionChanged += (_, _) => changed++;

        Publish(Full(MakeTorrent("second", 1, queue: 1), MakeTorrent("first", 2, queue: 0)));

        Assert.True(vm.IsSelected("first"));
        Assert.Equal(new[] { "first" }, vm.SelectedTorrents.Select(t => t.Hash));
        Assert.True(restored > 0);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Later_updates_leave_the_selection_to_the_user()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("a", 1, queue: 0), MakeTorrent("b", 2, queue: 1)));
        vm.SetSelection([]);

        Publish(Full(MakeTorrent("a", 1, queue: 0), MakeTorrent("b", 2, queue: 1)));

        Assert.Empty(vm.SelectedTorrents);
    }

    [Fact]
    public void A_server_without_torrents_does_not_select_one_added_later()
    {
        var vm = Create();
        Publish(Full());

        Publish(Delta([MakeTorrent("new", 1)]));

        Assert.Empty(vm.SelectedTorrents);
    }

    [Fact]
    public void Reconnecting_selects_the_top_row_again()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("a", 1, queue: 0)));
        vm.SetSelection([]);

        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connecting));
        _connection.StatusChanged += Raise.Event();
        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connected, "4.1.0"));
        Publish(Full(MakeTorrent("b", 2, queue: 0), MakeTorrent("a", 1, queue: 1)));

        Assert.Equal(new[] { "b" }, vm.SelectedTorrents.Select(t => t.Hash));
    }

    [Fact]
    public void The_top_row_respects_the_current_filter()
    {
        var vm = Create();
        vm.SelectedFilter = vm.Filters.Single(f => f.Filter == TorrentFilter.Seeding);

        Publish(Full(MakeTorrent("down", 1, status: TorrentStatus.Downloading, queue: 0),
            MakeTorrent("seed", 2, status: TorrentStatus.Seeding, done: 1, queue: 1)));

        Assert.Equal(new[] { "seed" }, vm.SelectedTorrents.Select(t => t.Hash));
    }

    [Fact]
    public void Labels_in_use_are_listed_by_name_with_counts()
    {
        var vm = Create();

        Publish(Full(MakeTorrent("a", 1, labels: ["linux", "iso"]), MakeTorrent("b", 2, labels: ["Linux2"]), MakeTorrent("c", 3, labels: ["linux"])));

        Assert.True(vm.HasLabels);
        Assert.Equal(new[] { "All labels", "iso", "linux", "Linux2" }, vm.LabelFilters.Select(l => l.Title));
        Assert.Equal(new[] { 3, 1, 2, 1 }, vm.LabelFilters.Select(l => l.Count));
        Assert.Equal(new[] { "iso", "linux", "Linux2" }, vm.KnownLabels);
    }

    [Fact]
    public void A_label_narrows_the_list_together_with_the_status_filter()
    {
        var vm = Create();
        Publish(Full(
            MakeTorrent("a", 1, status: TorrentStatus.Seeding, done: 1, labels: ["linux"]),
            MakeTorrent("b", 2, status: TorrentStatus.Downloading, labels: ["linux"]),
            MakeTorrent("c", 3, status: TorrentStatus.Seeding, done: 1)));

        vm.SelectedLabel = vm.LabelFilters.Single(l => l.Label == "linux");
        Assert.Equal(new[] { "a", "b" }, vm.VisibleTorrents.Select(t => t.Hash));

        vm.SelectedFilter = vm.Filters.Single(f => f.Filter == TorrentFilter.Seeding);
        Assert.Equal(new[] { "a" }, vm.VisibleTorrents.Select(t => t.Hash));
    }

    [Fact]
    public void When_the_chosen_label_disappears_all_torrents_show_again()
    {
        var vm = Create();
        Publish(Full(MakeTorrent("a", 1, labels: ["old"]), MakeTorrent("b", 2)));
        vm.SelectedLabel = vm.LabelFilters.Single(l => l.Label == "old");

        Publish(Full(MakeTorrent("a", 1), MakeTorrent("b", 2)));

        Assert.True(vm.SelectedLabel!.IsAll);
        Assert.False(vm.HasLabels);
        Assert.Equal(2, vm.VisibleTorrents.Count);
    }
}
