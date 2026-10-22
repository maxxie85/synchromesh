using NSubstitute;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.ViewModels;
using Synchro.ViewModels.Details;
using Xunit;
using static Synchro.Tests.TestData;

namespace Synchro.Tests;

public class TorrentDetailsViewModelTests
{
    private readonly ITorrentFeed _feed = Substitute.For<ITorrentFeed>();
    private readonly ITorrentDetailsFeed _details = Substitute.For<ITorrentDetailsFeed>();
    private readonly IConnectionService _connection = Substitute.For<IConnectionService>();
    private readonly ITorrentActions _actions = Substitute.For<ITorrentActions>();
    private readonly FixedTime _time = new(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
    private readonly TorrentListViewModel _list;

    public TorrentDetailsViewModelTests()
    {
        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connected, "4.1.3"));
        _list = new TorrentListViewModel(_feed, _connection);
        _feed.TorrentsUpdated += Raise.Event<EventHandler<TorrentUpdate>>(_feed,
            Full(MakeTorrent("a", 1, queue: 0), MakeTorrent("b", 2, queue: 1)));
        _list.SetSelection([]); // the first load selects the top row; these tests start with nothing selected
    }

    private readonly Synchro.ViewModels.Services.IDialogService _dialogs = Substitute.For<Synchro.ViewModels.Services.IDialogService>();

    private TorrentDetailsViewModel Create() => new(_details, _list, _time, _actions, _dialogs);

    private void Select(params string[] hashes) =>
        _list.SetSelection(_list.VisibleTorrents.Where(t => hashes.Contains(t.Hash)));

    private void Publish(string hash, TorrentDetails? details) =>
        _details.DetailsUpdated += Raise.Event<EventHandler<TorrentDetailsUpdate>>(_details, new TorrentDetailsUpdate(hash, details));

    [Fact]
    public void Without_a_selection_it_asks_the_user_to_select_a_torrent()
    {
        var vm = Create();

        Assert.False(vm.HasDetails);
        Assert.True(vm.HasMessage);
        Assert.Equal(TorrentDetailsViewModel.NothingSelectedMessage, vm.Message);
    }

    [Fact]
    public void Selecting_one_torrent_watches_it_and_shows_loading()
    {
        var vm = Create();

        Select("a");

        _details.Received(1).Watch("a", false);
        Assert.True(vm.IsLoading);
        Assert.False(vm.HasMessage);
    }

    [Fact]
    public void Selecting_several_torrents_stops_watching_and_says_how_many()
    {
        var vm = Create();
        Select("a");

        Select("a", "b");

        _details.Received().Watch(null, Arg.Any<bool>());
        Assert.Equal("2 torrents selected.", vm.Message);
        Assert.True(vm.HasMessage);
    }

    [Fact]
    public void Details_fill_all_three_tabs()
    {
        var vm = Create();
        Select("a");

        Publish("a", MakeDetails("a", trackers: [MakeTracker(1), MakeTracker(2)], peers: [MakePeer("10.0.0.1")]));

        Assert.True(vm.HasDetails);
        Assert.False(vm.IsLoading);
        Assert.Equal("Torrent a", vm.General.Name);
        Assert.Equal(50, vm.General.Progress);
        Assert.Contains(vm.General.TorrentRows, r => r.Label == "Location" && r.Value == "/data");
        Assert.Equal(new[] { 1, 2 }, vm.Trackers.Select(t => t.Id));
        Assert.Equal("10.0.0.1", Assert.Single(vm.Peers).Address);
        Assert.Equal("Trackers (2)", vm.TrackersHeader);
        Assert.Equal("Peers (1)", vm.PeersHeader);
    }

    [Fact]
    public void A_late_answer_for_the_previous_selection_is_ignored()
    {
        var vm = Create();
        Select("a");
        Select("b");

        Publish("a", MakeDetails("a"));

        Assert.False(vm.HasDetails);
        Assert.True(vm.IsLoading);
    }

    [Fact]
    public void A_torrent_that_disappeared_is_explained()
    {
        var vm = Create();
        Select("a");

        Publish("a", null);

        Assert.False(vm.HasDetails);
        Assert.Equal(TorrentDetailsViewModel.GoneMessage, vm.Message);
    }

    [Fact]
    public void Rows_are_updated_in_place_between_polls()
    {
        var vm = Create();
        Select("a");
        Publish("a", MakeDetails("a", peers: [MakePeer("10.0.0.1"), MakePeer("10.0.0.2")]));
        var first = vm.Peers.ToList();

        Publish("a", MakeDetails("a", peers: [MakePeer("10.0.0.1", down: 5000), MakePeer("10.0.0.2")]));

        Assert.Equal(first, vm.Peers); // same instances
        Assert.Equal(Synchro.ViewModels.Formatting.ByteFormatter.FormatRate(5000), vm.Peers[0].DownloadText);
    }

    [Fact]
    public void A_peer_that_leaves_is_removed_and_the_others_keep_their_rows()
    {
        var vm = Create();
        Select("a");
        Publish("a", MakeDetails("a", peers: [MakePeer("10.0.0.1"), MakePeer("10.0.0.2")]));
        var kept = vm.Peers[1];

        Publish("a", MakeDetails("a", peers: [MakePeer("10.0.0.2"), MakePeer("10.0.0.3")]));

        Assert.Equal(new[] { "10.0.0.2", "10.0.0.3" }, vm.Peers.Select(p => p.Address));
        Assert.Same(kept, vm.Peers[0]);
    }

    [Fact]
    public void Disposing_stops_watching()
    {
        Create().Dispose();

        _details.Received().Watch(null, Arg.Any<bool>());
    }

    [Fact]
    public void Files_are_fetched_only_while_the_files_tab_shows()
    {
        var vm = Create();
        Select("a");

        vm.SelectedTabIndex = TorrentDetailsViewModel.FilesTabIndex;
        vm.SelectedTabIndex = 0;

        Received.InOrder(() =>
        {
            _details.Watch("a", false);
            _details.Watch("a", true);
            _details.Watch("a", false);
        });
    }

    [Fact]
    public void Files_arrive_in_the_files_tab_and_its_header()
    {
        var vm = Create();
        Select("a");

        Publish("a", MakeDetails("a") with { Files = [new TorrentFile(0, "a/x.mkv", 10, 5, true, FilePriority.Normal)] });

        Assert.True(vm.Files.HasFiles);
        Assert.Equal("Files (1)", vm.FilesHeader);
    }

    [Fact]
    public void An_update_without_files_keeps_the_last_file_list()
    {
        var vm = Create();
        Select("a");
        Publish("a", MakeDetails("a") with { Files = [new TorrentFile(0, "a/x.mkv", 10, 5, true, FilePriority.Normal)] });

        Publish("a", MakeDetails("a"));

        Assert.True(vm.Files.HasFiles);
    }

    private static string Url(int id) => $"udp://tracker{id}.example:6969/announce";

    private TorrentDetailsViewModel WithTrackers()
    {
        var vm = Create();
        Select("a");
        Publish("a", MakeDetails("a", trackers: [MakeTracker(1, tier: 0), MakeTracker(2, tier: 0), MakeTracker(3, tier: 1)]));
        return vm;
    }

    [Fact]
    public async Task Added_trackers_each_get_a_new_tier_after_the_existing_ones()
    {
        var vm = WithTrackers();
        _dialogs.PromptAsync(Arg.Any<TextPrompt>()).Returns("https://new.example/announce\n\nudp://other.example:80\n");

        await vm.AddTrackersCommand.ExecuteAsync(null);

        await _actions.Received(1).SetTrackersAsync("a", Arg.Is<IReadOnlyList<IReadOnlyList<string>>>(t =>
            t.Count == 4
            && t[0].SequenceEqual(new[] { Url(1), Url(2) })
            && t[1].SequenceEqual(new[] { Url(3) })
            && t[2].SequenceEqual(new[] { "https://new.example/announce" })
            && t[3].SequenceEqual(new[] { "udp://other.example:80" })));
    }

    [Fact]
    public async Task Cancelling_the_add_changes_nothing()
    {
        var vm = WithTrackers();
        _dialogs.PromptAsync(Arg.Any<TextPrompt>()).Returns((string?)null);

        await vm.AddTrackersCommand.ExecuteAsync(null);

        await _actions.DidNotReceiveWithAnyArgs().SetTrackersAsync(default!, default!);
    }

    [Fact]
    public async Task Removing_a_tracker_sends_the_others_and_keeps_their_tiers()
    {
        var vm = WithTrackers();
        _dialogs.ConfirmAsync(default!, default!, default!).ReturnsForAnyArgs(true);

        await vm.RemoveTrackerCommand.ExecuteAsync(vm.Trackers[1]);

        await _actions.Received(1).SetTrackersAsync("a", Arg.Is<IReadOnlyList<IReadOnlyList<string>>>(t =>
            t.Count == 2 && t[0].SequenceEqual(new[] { Url(1) }) && t[1].SequenceEqual(new[] { Url(3) })));
    }

    [Fact]
    public async Task A_tracker_is_only_removed_after_confirming()
    {
        var vm = WithTrackers();
        _dialogs.ConfirmAsync(default!, default!, default!).ReturnsForAnyArgs(false);

        await vm.RemoveTrackerCommand.ExecuteAsync(vm.Trackers[0]);

        await _actions.DidNotReceiveWithAnyArgs().SetTrackersAsync(default!, default!);
    }

    [Fact]
    public async Task A_failed_tracker_change_is_explained()
    {
        var vm = WithTrackers();
        _dialogs.ConfirmAsync(default!, default!, default!).ReturnsForAnyArgs(true);
        _actions.SetTrackersAsync(default!, default!).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("Not connected")));

        await vm.RemoveTrackerCommand.ExecuteAsync(vm.Trackers[0]);

        await _dialogs.Received(1).ShowMessageAsync("Couldn't remove the tracker", "Not connected");
    }

    [Theory]
    [InlineData("ftp://x.example/announce", "Not a tracker address")]
    [InlineData("tracker.example:80", "Not a tracker address")]
    [InlineData("https://ok.example/a\nnonsense", "Not a tracker address: nonsense")]
    [InlineData("udp://tracker1.example:6969/announce", "already has")]
    public void Tracker_addresses_are_checked(string text, string error)
    {
        var (_, message) = TorrentDetailsViewModel.ParseTrackerAddresses(text, new HashSet<string>([Url(1)], StringComparer.OrdinalIgnoreCase));

        Assert.Contains(error, message);
    }

    [Fact]
    public void Repeated_tracker_addresses_are_added_once()
    {
        var (addresses, error) = TorrentDetailsViewModel.ParseTrackerAddresses(
            " https://a.example/announce \nhttps://a.example/announce", new HashSet<string>());

        Assert.Null(error);
        Assert.Equal(new[] { "https://a.example/announce" }, addresses);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
