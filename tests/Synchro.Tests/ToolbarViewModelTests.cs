using NSubstitute;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.ViewModels;
using Synchro.ViewModels.Services;
using Xunit;
using static Synchro.Tests.TestData;

namespace Synchro.Tests;

public class ToolbarViewModelTests
{
    private readonly ITorrentFeed _feed = Substitute.For<ITorrentFeed>();
    private readonly IConnectionService _connection = Substitute.For<IConnectionService>();
    private readonly ITorrentActions _actions = Substitute.For<ITorrentActions>();
    private readonly ISpeedLimitService _speedLimits = Substitute.For<ISpeedLimitService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IAddTorrentFlow _flow = Substitute.For<IAddTorrentFlow>();
    private readonly TorrentListViewModel _list;
    private readonly ToolbarViewModel _toolbar;

    public ToolbarViewModelTests()
    {
        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connected, "4.1.3"));
        _list = new TorrentListViewModel(_feed, _connection);
        _toolbar = new ToolbarViewModel(_list, _connection, _actions, _speedLimits, _dialogs, _flow);

        _feed.TorrentsUpdated += Raise.Event<EventHandler<TorrentUpdate>>(_feed, Full(
            MakeTorrent("a", 1, name: "Alpha", queue: 0),
            MakeTorrent("b", 2, name: "Beta", queue: 1),
            MakeTorrent("c", 3, name: "Gamma", queue: 2)));
        _list.SetSelection([]); // the first load selects the top row; these tests start with nothing selected
    }

    private void Select(params string[] hashes) =>
        _list.SetSelection(_list.VisibleTorrents.Where(t => hashes.Contains(t.Hash)));

    private static bool Hashes(IReadOnlyList<string> actual, params string[] expected) => actual.SequenceEqual(expected);

    [Fact]
    public void Commands_need_a_selection_and_a_connection()
    {
        Assert.False(_toolbar.StartCommand.CanExecute(null));

        Select("a");
        Assert.True(_toolbar.StartCommand.CanExecute(null));
        Assert.True(_toolbar.MoveToTopCommand.CanExecute(null));

        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Failed, Message: "lost"));
        Assert.False(_toolbar.StartCommand.CanExecute(null));
    }

    [Fact]
    public void Commands_are_re_evaluated_when_the_selection_changes()
    {
        var changed = 0;
        _toolbar.StopCommand.CanExecuteChanged += (_, _) => changed++;

        Select("a");

        Assert.True(changed > 0);
    }

    [Fact]
    public async Task Start_acts_on_the_selected_hashes_in_queue_order()
    {
        Select("c", "a");

        await _toolbar.StartCommand.ExecuteAsync(null);

        await _actions.Received(1).StartAsync(Arg.Is<IReadOnlyList<string>>(h => Hashes(h, "a", "c")), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Start_now_skips_the_queue()
    {
        Select("a");

        await _toolbar.StartNowCommand.ExecuteAsync(null);

        await _actions.Received(1).StartAsync(Arg.Any<IReadOnlyList<string>>(), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stop_acts_on_the_selection()
    {
        Select("b");

        await _toolbar.StopCommand.ExecuteAsync(null);

        await _actions.Received(1).StopAsync(Arg.Is<IReadOnlyList<string>>(h => Hashes(h, "b")), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Remove_asks_first_and_passes_on_the_delete_choice(bool deleteData)
    {
        Select("a", "b");
        _dialogs.ConfirmRemoveAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new RemoveRequest(deleteData));

        await _toolbar.RemoveCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ConfirmRemoveAsync(
            Arg.Is<IReadOnlyList<string>>(n => Hashes(n, "Alpha", "Beta")), Arg.Any<CancellationToken>());
        await _actions.Received(1).RemoveAsync(
            Arg.Is<IReadOnlyList<string>>(h => Hashes(h, "a", "b")), deleteData, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancelling_the_confirmation_removes_nothing()
    {
        Select("a");
        _dialogs.ConfirmRemoveAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns((RemoveRequest?)null);

        await _toolbar.RemoveCommand.ExecuteAsync(null);

        await _actions.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default, default);
    }

    [Theory]
    [InlineData(nameof(ToolbarViewModel.MoveToTopCommand), QueueMove.Top)]
    [InlineData(nameof(ToolbarViewModel.MoveUpCommand), QueueMove.Up)]
    [InlineData(nameof(ToolbarViewModel.MoveDownCommand), QueueMove.Down)]
    [InlineData(nameof(ToolbarViewModel.MoveToBottomCommand), QueueMove.Bottom)]
    public async Task Queue_commands_move_the_selection(string commandName, QueueMove expected)
    {
        Select("b");
        var command = (CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)typeof(ToolbarViewModel).GetProperty(commandName)!.GetValue(_toolbar)!;

        await command.ExecuteAsync(null);

        await _actions.Received(1).MoveInQueueAsync(Arg.Is<IReadOnlyList<string>>(h => Hashes(h, "b")), expected, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failing_action_shows_an_error_instead_of_crashing()
    {
        Select("a");
        _actions.StopAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("server said no")));

        await _toolbar.StopCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ShowMessageAsync(Arg.Any<string>(), "server said no", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Speed_limits_open_without_waiting_and_saving_sends_the_edit()
    {
        var current = new SpeedLimits(false, 100, false, 50, false);
        var edited = new SpeedLimits(true, 500, true, 25, false);
        _speedLimits.GetAsync(Arg.Any<CancellationToken>()).Returns(current);
        Func<CancellationToken, Task<SpeedLimits>>? load = null;
        _dialogs.ShowSpeedLimitsAsync(Arg.Do<Func<CancellationToken, Task<SpeedLimits>>>(l => load = l), Arg.Any<CancellationToken>())
            .Returns(edited);

        await _toolbar.SpeedLimitsCommand.ExecuteAsync(null);

        await _speedLimits.DidNotReceiveWithAnyArgs().GetAsync(default); // the dialog loads them, not the click
        Assert.Equal(current, await load!(CancellationToken.None));
        await _speedLimits.Received(1).SetAsync(edited, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancelling_the_speed_limit_dialog_changes_nothing()
    {
        _dialogs.ShowSpeedLimitsAsync(Arg.Any<Func<CancellationToken, Task<SpeedLimits>>>(), Arg.Any<CancellationToken>())
            .Returns((SpeedLimits?)null);

        await _toolbar.SpeedLimitsCommand.ExecuteAsync(null);

        await _speedLimits.DidNotReceiveWithAnyArgs().SetAsync(default!, default);
    }

    [Fact]
    public async Task A_failed_save_of_the_speed_limits_is_shown()
    {
        _dialogs.ShowSpeedLimitsAsync(Arg.Any<Func<CancellationToken, Task<SpeedLimits>>>(), Arg.Any<CancellationToken>())
            .Returns(new SpeedLimits(true, 500, false, 50, false));
        _speedLimits.SetAsync(Arg.Any<SpeedLimits>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("offline")));

        await _toolbar.SpeedLimitsCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ShowMessageAsync(Arg.Any<string>(), "offline", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Add_opens_the_add_flow()
    {
        await _toolbar.AddCommand.ExecuteAsync(null);

        await _flow.Received(1).AddManuallyAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Paste_adds_from_the_clipboard()
    {
        await _toolbar.PasteCommand.ExecuteAsync(null);

        await _flow.Received(1).AddFromClipboardAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Add_and_paste_need_a_connection_but_not_a_selection()
    {
        Assert.True(_toolbar.AddCommand.CanExecute(null));
        Assert.True(_toolbar.PasteCommand.CanExecute(null));

        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Disconnected));

        Assert.False(_toolbar.AddCommand.CanExecute(null));
        Assert.False(_toolbar.PasteCommand.CanExecute(null));
    }

    [Fact]
    public async Task Dropped_sources_go_through_the_same_flow()
    {
        var sources = new AddTorrentSource[] { new AddTorrentSource.Magnet("magnet:?xt=urn:btih:abc", null) };

        await _toolbar.AddSourcesAsync(sources);

        await _flow.Received(1).AddAsync(sources, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failing_add_flow_shows_a_message_instead_of_crashing()
    {
        _flow.AddFromClipboardAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("clipboard unavailable")));

        await _toolbar.PasteCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ShowMessageAsync(Arg.Any<string>(), "clipboard unavailable", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Verify_and_reannounce_act_on_the_selection()
    {
        Select("b", "a");

        await _toolbar.VerifyCommand.ExecuteAsync(null);
        await _toolbar.ReannounceCommand.ExecuteAsync(null);

        await _actions.Received(1).VerifyAsync(Arg.Is<IReadOnlyList<string>>(h => Hashes(h, "a", "b")), Arg.Any<CancellationToken>());
        await _actions.Received(1).ReannounceAsync(Arg.Is<IReadOnlyList<string>>(h => Hashes(h, "a", "b")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Verify_needs_a_selection()
    {
        Assert.False(_toolbar.VerifyCommand.CanExecute(null));
        Assert.False(_toolbar.ReannounceCommand.CanExecute(null));
    }

    [Fact]
    public async Task Move_data_starts_from_the_current_folder_and_moves_the_selection()
    {
        Select("a", "b");
        _actions.GetLocationAsync("a", Arg.Any<CancellationToken>()).Returns("/data/downloads");
        _dialogs.ShowMoveDataAsync(Arg.Any<IReadOnlyList<string>>(), "/data/downloads", Arg.Any<CancellationToken>())
            .Returns(new MoveDataRequest("/data/movies", MoveFiles: true));

        await _toolbar.MoveDataCommand.ExecuteAsync(null);

        await _actions.Received(1).MoveDataAsync(Arg.Is<IReadOnlyList<string>>(h => Hashes(h, "a", "b")),
            "/data/movies", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancelling_the_move_changes_nothing()
    {
        Select("a");
        _dialogs.ShowMoveDataAsync(default!, default!, default).ReturnsForAnyArgs((MoveDataRequest?)null);

        await _toolbar.MoveDataCommand.ExecuteAsync(null);

        await _actions.DidNotReceiveWithAnyArgs().MoveDataAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Edit_labels_applies_the_changed_torrents()
    {
        Select("a");
        IReadOnlyDictionary<string, IReadOnlyList<string>> changed = new Dictionary<string, IReadOnlyList<string>> { ["a"] = ["movies"] };
        _dialogs.ShowLabelsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>>(), Arg.Any<CancellationToken>())
            .Returns(changed);

        await _toolbar.EditLabelsCommand.ExecuteAsync(null);

        await _actions.Received(1).SetLabelsAsync(changed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rename_renames_the_one_selected_torrent_by_its_name()
    {
        Select("b");
        _dialogs.PromptAsync(Arg.Any<TextPrompt>()).Returns("Beta (2026)");

        await _toolbar.RenameCommand.ExecuteAsync(null);

        await _dialogs.Received(1).PromptAsync(Arg.Is<TextPrompt>(p => p.InitialText == "Beta" && p.Title == "Rename torrent"), Arg.Any<CancellationToken>());
        await _actions.Received(1).RenamePathAsync("b", "Beta", "Beta (2026)", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Rename_needs_exactly_one_torrent()
    {
        Assert.False(_toolbar.RenameCommand.CanExecute(null));
        Select("a", "b");
        Assert.False(_toolbar.RenameCommand.CanExecute(null));
        Select("a");
        Assert.True(_toolbar.RenameCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_failed_rename_is_explained()
    {
        Select("a");
        _dialogs.PromptAsync(Arg.Any<TextPrompt>()).Returns("New");
        _actions.RenamePathAsync(default!, default!, default!).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("Invalid argument")));

        await _toolbar.RenameCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ShowMessageAsync("Couldn't rename the torrent", "Invalid argument", Arg.Any<CancellationToken>());
    }
}
