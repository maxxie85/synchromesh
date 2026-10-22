using NSubstitute;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.ViewModels;
using Synchro.ViewModels.Details;
using Synchro.ViewModels.Services;
using Xunit;

namespace Synchro.Tests;

/// <summary>Magnet links and URLs: add paused, wait for the file list, choose, then start.</summary>
public class AddTorrentLinkFileChoiceTests
{
    private readonly IAddTorrentService _service = Substitute.For<IAddTorrentService>();
    private static readonly AddTorrentResult Added = new(AddTorrentOutcome.Added, "abc", "Ubuntu");
    private static readonly AddTorrentSource.Magnet Magnet = new("magnet:?xt=urn:btih:abc&dn=Ubuntu", "Ubuntu");

    private static readonly TorrentFileList NoMetadataYet = new("Ubuntu", 0.4, []);
    private static readonly TorrentFileList Metadata = new("Ubuntu", 1,
    [
        new TorrentFile(0, "Ubuntu/ubuntu.iso", 6_000_000_000, 0, true, FilePriority.Normal),
        new TorrentFile(1, "Ubuntu/.pad/1024", 1024, 0, true, FilePriority.Normal),
        new TorrentFile(2, "Ubuntu/README.txt", 900, 0, true, FilePriority.Normal),
    ]);

    private AddTorrentResult? _closedWith;
    private bool _closed;

    public AddTorrentLinkFileChoiceTests()
    {
        _service.GetDefaultDownloadDirectoryAsync(Arg.Any<CancellationToken>()).Returns("/downloads");
        _service.AddAsync(Arg.Any<AddTorrentRequest>(), Arg.Any<CancellationToken>()).Returns(Added);
    }

    private async Task<AddTorrentViewModel> StartAsync(params TorrentFileList?[] answers)
    {
        if (answers.Length > 0)
            _service.GetFileListAsync("abc", Arg.Any<CancellationToken>()).Returns(answers[0], answers[1..]);

        var vm = new AddTorrentViewModel(_service, Substitute.For<IFilePicker>())
        {
            FileListPollInterval = TimeSpan.FromMilliseconds(1),
        };
        vm.CloseRequested += (_, r) => { _closed = true; _closedWith = r; };
        await vm.InitializeAsync(Magnet);
        return vm;
    }

    [Fact]
    public async Task A_magnet_is_added_running_so_it_can_get_its_file_list_and_the_dialog_waits()
    {
        var vm = await StartAsync(NoMetadataYet, NoMetadataYet, NoMetadataYet);
        Assert.Equal("Next", vm.AddButtonText);

        await vm.AddCommand.ExecuteAsync(null);

        // A stopped torrent contacts no peers, so a paused magnet would never get its file list.
        await _service.Received(1).AddAsync(Arg.Is<AddTorrentRequest>(r => !r.Paused), Arg.Any<CancellationToken>());
        Assert.True(vm.IsFetchingFiles);
        Assert.Equal("Cancel adding", vm.CancelButtonText);
        await TestWait.UntilAsync(() => vm.FetchText.Contains("40%"));
        Assert.False(_closed);
    }

    [Fact]
    public async Task A_magnet_with_dont_start_yet_still_runs_until_the_list_is_there()
    {
        var vm = await StartAsync(NoMetadataYet);
        vm.StartPaused = true;

        await vm.AddCommand.ExecuteAsync(null);

        await _service.Received(1).AddAsync(Arg.Is<AddTorrentRequest>(r => !r.Paused), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_torrent_url_is_added_paused_and_never_needs_stopping()
    {
        var vm = await StartAsync(Metadata);
        vm.LinkText = "https://example.org/ubuntu.torrent";

        await vm.AddCommand.ExecuteAsync(null);
        await TestWait.UntilAsync(() => vm.Stage == AddStage.ChoosingFiles);

        await _service.Received(1).AddAsync(Arg.Is<AddTorrentRequest>(r => r.Paused), Arg.Any<CancellationToken>());
        await _service.DidNotReceiveWithAnyArgs().HoldForChoiceAsync(default!, default);
    }

    [Fact]
    public async Task The_magnet_is_stopped_as_soon_as_its_file_list_arrives()
    {
        var vm = await StartAsync(NoMetadataYet, Metadata);

        await vm.AddCommand.ExecuteAsync(null);
        await TestWait.UntilAsync(() => vm.Stage == AddStage.ChoosingFiles);

        await _service.Received(1).HoldForChoiceAsync("abc", Arg.Any<CancellationToken>());
        await _service.DidNotReceiveWithAnyArgs().FinishAddAsync(default!, default, default, default);
    }

    [Fact]
    public async Task If_stopping_fails_the_user_is_told_and_can_still_choose()
    {
        _service.HoldForChoiceAsync("abc", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("server busy")));
        var vm = await StartAsync(Metadata);

        await vm.AddCommand.ExecuteAsync(null);
        await TestWait.UntilAsync(() => vm.Stage == AddStage.ChoosingFiles);

        Assert.Contains("server busy", vm.StatusMessage);
    }

    [Fact]
    public async Task Once_the_file_list_arrives_the_files_can_be_chosen_without_padding()
    {
        var vm = await StartAsync(NoMetadataYet, Metadata);

        await vm.AddCommand.ExecuteAsync(null);
        await TestWait.UntilAsync(() => vm.Stage == AddStage.ChoosingFiles);

        Assert.Equal(new[] { "README.txt", "ubuntu.iso" }, vm.Files!.Rows.Select(r => r.Name));
        Assert.Equal("Start download", vm.AddButtonText);
        Assert.True(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_file_list_that_is_there_at_once_can_still_be_confirmed()
    {
        var vm = await StartAsync(Metadata); // the server answers immediately (a URL, or cached metadata)

        await vm.AddCommand.ExecuteAsync(null);
        await TestWait.UntilAsync(() => vm.Stage == AddStage.ChoosingFiles);

        Assert.True(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public async Task Choices_are_applied_then_the_torrent_starts_and_the_dialog_closes()
    {
        var vm = await StartAsync(Metadata);
        await vm.AddCommand.ExecuteAsync(null);
        await TestWait.UntilAsync(() => vm.Stage == AddStage.ChoosingFiles);
        vm.Files!.Rows.Single(r => r.Name == "README.txt").IsWanted = false;

        await vm.AddCommand.ExecuteAsync(null);

        await _service.Received(1).FinishAddAsync("abc",
            Arg.Is<FileChoices?>(c => c != null && c.Unwanted.SequenceEqual(new[] { 2 })), true, Arg.Any<CancellationToken>());
        Assert.Equal(Added, _closedWith);
    }

    [Fact]
    public async Task Dont_start_yet_keeps_it_paused_after_choosing()
    {
        var vm = await StartAsync(Metadata);
        vm.StartPaused = true;
        await vm.AddCommand.ExecuteAsync(null);
        await TestWait.UntilAsync(() => vm.Stage == AddStage.ChoosingFiles);

        Assert.Equal("Add", vm.AddButtonText);
        await vm.AddCommand.ExecuteAsync(null);

        await _service.Received(1).FinishAddAsync("abc", null, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_single_file_needs_no_choice_and_starts_right_away()
    {
        var vm = await StartAsync(new TorrentFileList("u.iso", 1, [new TorrentFile(0, "u.iso", 10, 0, true, FilePriority.Normal)]));

        await vm.AddCommand.ExecuteAsync(null);
        await TestWait.UntilAsync(() => _closed);

        await _service.Received(1).FinishAddAsync("abc", null, true, Arg.Any<CancellationToken>());
        Assert.Equal(Added, _closedWith);
    }

    [Fact]
    public async Task Download_everything_skips_the_wait()
    {
        var vm = await StartAsync(NoMetadataYet);
        await vm.AddCommand.ExecuteAsync(null);

        await vm.DownloadEverythingCommand.ExecuteAsync(null);

        await _service.Received(1).FinishAddAsync("abc", null, true, Arg.Any<CancellationToken>());
        Assert.Equal(Added, _closedWith);
    }

    [Fact]
    public async Task Cancelling_after_the_add_takes_the_torrent_back()
    {
        var vm = await StartAsync(NoMetadataYet);
        await vm.AddCommand.ExecuteAsync(null);

        await vm.CancelCommand.ExecuteAsync(null);

        await _service.Received(1).UndoAddAsync("abc", Arg.Any<CancellationToken>());
        Assert.True(_closed);
        Assert.Null(_closedWith);
    }

    [Fact]
    public async Task Closing_the_window_after_finishing_takes_nothing_back()
    {
        var vm = await StartAsync(Metadata);
        await vm.AddCommand.ExecuteAsync(null);
        await TestWait.UntilAsync(() => vm.Stage == AddStage.ChoosingFiles);
        await vm.AddCommand.ExecuteAsync(null);

        await vm.AbandonAsync();

        await _service.DidNotReceiveWithAnyArgs().UndoAddAsync(default!, default);
    }

    [Fact]
    public async Task A_duplicate_is_left_alone()
    {
        var duplicate = new AddTorrentResult(AddTorrentOutcome.Duplicate, "abc", "Ubuntu");
        _service.AddAsync(Arg.Any<AddTorrentRequest>(), Arg.Any<CancellationToken>()).Returns(duplicate);
        var vm = await StartAsync();

        await vm.AddCommand.ExecuteAsync(null);
        await vm.AbandonAsync();

        Assert.Equal(duplicate, _closedWith);
        await _service.DidNotReceiveWithAnyArgs().GetFileListAsync(default!, default);
        await _service.DidNotReceiveWithAnyArgs().UndoAddAsync(default!, default);
    }

    [Fact]
    public async Task A_torrent_removed_while_waiting_is_explained()
    {
        var vm = await StartAsync([null]);

        await vm.AddCommand.ExecuteAsync(null);
        await TestWait.UntilAsync(() => vm.HasStatus);

        Assert.Contains("removed from the server", vm.StatusMessage);
        Assert.False(vm.DownloadEverythingCommand.CanExecute(null));
    }

    [Fact]
    public async Task With_the_option_off_a_link_is_added_directly()
    {
        var vm = await StartAsync();
        vm.ChooseFilesFirst = false;
        Assert.Equal("Add", vm.AddButtonText);

        await vm.AddCommand.ExecuteAsync(null);

        await _service.Received(1).AddAsync(Arg.Is<AddTorrentRequest>(r => !r.Paused), Arg.Any<CancellationToken>());
        Assert.Equal(Added, _closedWith);
    }
}
