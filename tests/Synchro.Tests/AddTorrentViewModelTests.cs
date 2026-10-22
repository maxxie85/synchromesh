using NSubstitute;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.ViewModels;
using Synchro.ViewModels.Services;
using Xunit;

namespace Synchro.Tests;

public class AddTorrentViewModelTests
{
    private readonly IAddTorrentService _service = Substitute.For<IAddTorrentService>();
    private readonly IFilePicker _picker = Substitute.For<IFilePicker>();
    private static readonly AddTorrentResult Added = new(AddTorrentOutcome.Added, "abc", "Ubuntu");
    private static readonly AddTorrentSource.Magnet Magnet = new("magnet:?xt=urn:btih:abc&dn=Ubuntu", "Ubuntu");

    public AddTorrentViewModelTests()
    {
        _service.GetDefaultDownloadDirectoryAsync(Arg.Any<CancellationToken>()).Returns("/downloads");
        _service.GetFreeSpaceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(5_000_000_000L);
        _service.AddAsync(Arg.Any<AddTorrentRequest>(), Arg.Any<CancellationToken>()).Returns(Added);
    }

    private AddTorrentViewModel Create() => new(_service, _picker);

    [Fact]
    public async Task A_magnet_link_fills_the_link_box()
    {
        var vm = Create();

        await vm.InitializeAsync(Magnet);

        Assert.Equal(Magnet.Link, vm.LinkText);
        Assert.True(vm.IsLinkMode);
        Assert.True(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_torrent_file_switches_to_file_mode()
    {
        var vm = Create();

        await vm.InitializeAsync(new AddTorrentSource.TorrentFile("ubuntu.torrent", [1, 2, 3]));

        Assert.True(vm.HasFile);
        Assert.False(vm.IsLinkMode);
        Assert.Equal("ubuntu.torrent", vm.FileSummary);
        Assert.True(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public async Task Loads_the_servers_default_folder_and_its_free_space()
    {
        var vm = Create();

        await vm.InitializeAsync(null);

        Assert.Equal("/downloads", vm.DownloadDirectory);
        Assert.True(vm.HasFreeSpace);
        Assert.Contains("GB", vm.FreeSpaceText);
    }

    [Fact]
    public async Task Still_works_when_the_default_folder_cannot_be_read()
    {
        _service.GetDefaultDownloadDirectoryAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new HttpRequestException("offline")));
        var vm = Create();

        await vm.InitializeAsync(Magnet);

        Assert.Equal("", vm.DownloadDirectory);
        Assert.True(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_failed_free_space_check_shows_a_note_not_an_error()
    {
        _service.GetFreeSpaceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<long>(new InvalidOperationException("no such folder")));
        var vm = Create();

        await vm.InitializeAsync(null);

        Assert.Contains("Couldn't check", vm.FreeSpaceText);
        Assert.False(vm.HasStatus);
    }

    [Fact]
    public void Adding_needs_a_valid_link_or_a_file()
    {
        var vm = Create();
        Assert.False(vm.AddCommand.CanExecute(null));

        vm.LinkText = "not a link";
        Assert.False(vm.AddCommand.CanExecute(null));
        Assert.True(vm.HasLinkError);

        vm.LinkText = "magnet:?xt=urn:btih:abc";
        Assert.True(vm.AddCommand.CanExecute(null));
        Assert.False(vm.HasLinkError);
    }

    [Fact]
    public async Task Sends_sequential_download_when_ticked()
    {
        AddTorrentRequest? sent = null;
        _service.AddAsync(Arg.Do<AddTorrentRequest>(r => sent = r), Arg.Any<CancellationToken>()).Returns(Added);
        var vm = Create();
        await vm.InitializeAsync(Magnet);
        vm.ChooseFilesFirst = false;
        Assert.False(vm.SequentialDownload);

        vm.SequentialDownload = true;
        await vm.AddCommand.ExecuteAsync(null);

        Assert.True(sent?.SequentialDownload);
    }

    [Fact]
    public async Task Leaves_the_folder_to_the_server_when_it_was_not_changed()
    {
        AddTorrentRequest? sent = null;
        _service.AddAsync(Arg.Do<AddTorrentRequest>(r => sent = r), Arg.Any<CancellationToken>()).Returns(Added);
        var vm = Create();
        await vm.InitializeAsync(Magnet);
        vm.ChooseFilesFirst = false;

        await vm.AddCommand.ExecuteAsync(null);

        Assert.NotNull(sent);
        Assert.Null(sent.DownloadDirectory);
        Assert.False(sent.Paused);
        Assert.Equal(Magnet, sent.Source);
    }

    [Fact]
    public async Task Sends_the_folder_and_paused_flag_when_the_user_changed_them()
    {
        AddTorrentRequest? sent = null;
        _service.AddAsync(Arg.Do<AddTorrentRequest>(r => sent = r), Arg.Any<CancellationToken>()).Returns(Added);
        var vm = Create();
        await vm.InitializeAsync(Magnet);

        vm.ChooseFilesFirst = false; // a direct add; choosing first runs a magnet to fetch its file list
        vm.DownloadDirectory = " /data/movies ";
        vm.StartPaused = true;
        await vm.AddCommand.ExecuteAsync(null);

        Assert.NotNull(sent);
        Assert.Equal("/data/movies", sent.DownloadDirectory);
        Assert.True(sent.Paused);
    }

    [Fact]
    public async Task Closes_with_the_result_after_adding()
    {
        var vm = Create();
        await vm.InitializeAsync(Magnet);
        vm.ChooseFilesFirst = false;
        AddTorrentResult? closedWith = null;
        vm.CloseRequested += (_, r) => closedWith = r;

        await vm.AddCommand.ExecuteAsync(null);

        Assert.Equal(Added, closedWith);
    }

    [Fact]
    public async Task A_rejected_torrent_stays_open_with_the_servers_message()
    {
        _service.AddAsync(Arg.Any<AddTorrentRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AddTorrentResult>(new InvalidOperationException("invalid or corrupt torrent file")));
        var vm = Create();
        await vm.InitializeAsync(Magnet);
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        await vm.AddCommand.ExecuteAsync(null);

        Assert.False(closed);
        Assert.Contains("invalid or corrupt torrent file", vm.StatusMessage);
        Assert.False(vm.IsAdding);
        Assert.True(vm.AddCommand.CanExecute(null)); // the user can fix it and try again
    }

    [Fact]
    public async Task Choosing_a_file_switches_to_file_mode()
    {
        var file = new AddTorrentSource.TorrentFile("picked.torrent", [9]);
        _picker.PickTorrentFileAsync(Arg.Any<CancellationToken>()).Returns(file);
        var vm = Create();

        await vm.ChooseFileCommand.ExecuteAsync(null);

        Assert.True(vm.HasFile);
        Assert.Equal("picked.torrent", vm.FileSummary);
    }

    [Fact]
    public async Task Cancelling_the_file_picker_changes_nothing()
    {
        _picker.PickTorrentFileAsync(Arg.Any<CancellationToken>()).Returns((AddTorrentSource.TorrentFile?)null);
        var vm = Create();

        await vm.ChooseFileCommand.ExecuteAsync(null);

        Assert.False(vm.HasFile);
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_shows_a_message()
    {
        _picker.PickTorrentFileAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AddTorrentSource.TorrentFile?>(new InvalidOperationException("too large")));
        var vm = Create();

        await vm.ChooseFileCommand.ExecuteAsync(null);

        Assert.Contains("too large", vm.StatusMessage);
        Assert.False(vm.HasFile);
    }

    [Fact]
    public async Task Using_a_link_instead_goes_back_to_link_mode()
    {
        var vm = Create();
        await vm.InitializeAsync(new AddTorrentSource.TorrentFile("a.torrent", [1]));

        vm.RemoveFileCommand.Execute(null);

        Assert.False(vm.HasFile);
        Assert.True(vm.IsLinkMode);
    }

    [Fact]
    public async Task The_file_wins_over_text_left_in_the_link_box()
    {
        AddTorrentRequest? sent = null;
        _service.AddAsync(Arg.Do<AddTorrentRequest>(r => sent = r), Arg.Any<CancellationToken>()).Returns(Added);
        var vm = Create();
        vm.LinkText = "magnet:?xt=urn:btih:old";
        await vm.InitializeAsync(new AddTorrentSource.TorrentFile("a.torrent", [1]));

        await vm.AddCommand.ExecuteAsync(null);

        Assert.IsType<AddTorrentSource.TorrentFile>(sent!.Source);
    }

    [Fact]
    public void Cancel_closes_with_null()
    {
        var vm = Create();
        AddTorrentResult? closedWith = Added;
        vm.CloseRequested += (_, r) => closedWith = r;

        vm.CancelCommand.Execute(null);

        Assert.Null(closedWith);
    }

    [Fact]
    public async Task Free_space_can_be_checked_for_any_folder()
    {
        var vm = Create();

        await vm.RefreshFreeSpaceAsync("/other");

        await _service.Received(1).GetFreeSpaceAsync("/other", Arg.Any<CancellationToken>());
        Assert.True(vm.HasFreeSpace);
    }

    [Fact]
    public async Task An_empty_folder_clears_the_free_space_note()
    {
        var vm = Create();
        await vm.RefreshFreeSpaceAsync("/other");

        await vm.RefreshFreeSpaceAsync("  ");

        Assert.False(vm.HasFreeSpace);
    }

    private static AddTorrentSource.TorrentFile MultiFileTorrent() =>
        new("mayday.torrent", TorrentBuilder.MultiFile("Mayday",
            (["Mayday.mkv"], 8_000_000_000), (["Mayday.nfo"], 900), (["Screens", "s1.jpg"], 200_000)));

    [Fact]
    public async Task A_multi_file_torrent_shows_its_files_all_ticked()
    {
        var vm = Create();

        await vm.InitializeAsync(MultiFileTorrent());

        Assert.True(vm.HasFileChoice);
        Assert.Equal(3, vm.Files!.WantedCount);
        Assert.False(vm.Files.ShowsProgress);
        Assert.Equal(new[] { "Screens", "Mayday.mkv", "Mayday.nfo" }, vm.Files.Rows.Select(r => r.Name));
    }

    [Fact]
    public async Task A_single_file_or_unreadable_torrent_has_no_file_choice()
    {
        var single = Create();
        await single.InitializeAsync(new AddTorrentSource.TorrentFile("u.torrent", TorrentBuilder.SingleFile("u.iso", 10)));
        var broken = Create();
        await broken.InitializeAsync(new AddTorrentSource.TorrentFile("x.torrent", [1, 2, 3]));

        Assert.False(single.HasFileChoice);
        Assert.False(broken.HasFileChoice);
        Assert.True(broken.AddCommand.CanExecute(null)); // still addable, with all files
    }

    [Fact]
    public async Task Choices_go_with_the_add_request_by_file_index()
    {
        var vm = Create();
        await vm.InitializeAsync(MultiFileTorrent());
        vm.Files!.Rows.Single(r => r.Name == "Mayday.nfo").IsWanted = false;
        vm.Files.Rows.Single(r => r.Name == "Mayday.mkv").SetPriorityCommand.Execute(Synchro.ViewModels.Details.PriorityOption.High);

        await vm.AddCommand.ExecuteAsync(null);

        await _service.Received(1).AddAsync(Arg.Is<AddTorrentRequest>(r =>
            r.Files != null
            && r.Files.Unwanted.SequenceEqual(new[] { 1 })
            && r.Files.HighPriority.SequenceEqual(new[] { 0 })
            && r.Files.LowPriority.Count == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Untouched_choices_send_no_file_lists()
    {
        var vm = Create();
        await vm.InitializeAsync(MultiFileTorrent());

        await vm.AddCommand.ExecuteAsync(null);

        await _service.Received(1).AddAsync(Arg.Is<AddTorrentRequest>(r => r.Files == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unticking_every_file_blocks_adding_until_one_is_ticked_again()
    {
        var vm = Create();
        await vm.InitializeAsync(MultiFileTorrent());
        var files = vm.Files!;

        foreach (var row in files.Rows.ToList()) row.IsWanted = false;
        Assert.True(files.NothingWanted);
        Assert.False(vm.AddCommand.CanExecute(null));

        files.Rows.Single(r => r.Name == "Mayday.mkv").IsWanted = true;
        Assert.True(vm.AddCommand.CanExecute(null));
    }

    [Fact]
    public async Task Padding_files_are_hidden_but_do_not_shift_indices()
    {
        var torrent = TorrentBuilder.Encode(new Dictionary<string, object>
        {
            ["info"] = new Dictionary<string, object>
            {
                ["name"] = "T",
                ["files"] = new List<object>
                {
                    new Dictionary<string, object> { ["length"] = 10L, ["path"] = new List<object> { "a.bin" } },
                    new Dictionary<string, object> { ["length"] = 6L, ["attr"] = "p", ["path"] = new List<object> { ".pad", "6" } },
                    new Dictionary<string, object> { ["length"] = 4L, ["path"] = new List<object> { "b.bin" } },
                },
            },
        });
        var vm = Create();
        await vm.InitializeAsync(new AddTorrentSource.TorrentFile("t.torrent", torrent));

        Assert.Equal(new[] { "a.bin", "b.bin" }, vm.Files!.Rows.Select(r => r.Name));
        vm.Files.Rows.Single(r => r.Name == "b.bin").IsWanted = false;

        Assert.Equal(new[] { 2 }, vm.Files.GetChoices().Unwanted);
    }

    [Fact]
    public async Task Removing_the_file_drops_its_choices()
    {
        var vm = Create();
        await vm.InitializeAsync(MultiFileTorrent());

        vm.RemoveFileCommand.Execute(null);

        Assert.False(vm.HasFileChoice);
    }
}
