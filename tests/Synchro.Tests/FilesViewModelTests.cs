using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.ViewModels;
using Synchro.ViewModels.Details;
using Synchro.ViewModels.Services;
using Xunit;

namespace Synchro.Tests;

public class FilesViewModelTests
{
    private readonly ITorrentActions _actions = Substitute.For<ITorrentActions>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    private FilesViewModel Create() => new(_actions, _dialogs);

    private static TorrentFile MakeFile(int index, string path, bool wanted = true,
        FilePriority priority = FilePriority.Normal, long length = 100, long done = 50) =>
        new(index, path, length, done, wanted, priority);

    // Root folder "T" holds a.mkv, b.nfo and a Screens folder with s10 and s2.
    private static readonly TorrentFile[] Files =
    [
        MakeFile(0, "T/b.nfo"),
        MakeFile(1, "T/Screens/s10.jpg", wanted: false, priority: FilePriority.Low),
        MakeFile(2, "T/a.mkv", priority: FilePriority.High),
        MakeFile(3, "T/Screens/s2.jpg"),
    ];

    private static FileNodeViewModel Row(FilesViewModel vm, string name) => vm.Rows.Single(r => r.Name == name);

    [Fact]
    public void The_shared_root_folder_is_left_out_and_folders_come_first()
    {
        var vm = Create();

        vm.Apply("h", Files);

        Assert.Equal(new[] { "Screens", "a.mkv", "b.nfo" }, vm.Rows.Select(r => r.Name));
        Assert.True(vm.HasFiles);
        Assert.Equal(4, vm.FileCount);
    }

    [Fact]
    public void Expanding_a_folder_shows_its_files_in_natural_order()
    {
        var vm = Create();
        vm.Apply("h", Files);

        Row(vm, "Screens").IsExpanded = true;

        Assert.Equal(new[] { "Screens", "s2.jpg", "s10.jpg", "a.mkv", "b.nfo" }, vm.Rows.Select(r => r.Name));
        Assert.Equal(1, Row(vm, "s2.jpg").Depth);
    }

    [Fact]
    public void A_folder_adds_up_its_files_and_shows_mixed_states()
    {
        var vm = Create();

        vm.Apply("h", Files);

        var screens = Row(vm, "Screens");
        Assert.Null(screens.IsWanted);
        Assert.Null(screens.Priority);
        Assert.Equal("Mixed", screens.PriorityText);
        Assert.Equal(50, screens.Progress);
        Assert.Equal(new[] { 3, 1 }, screens.FileIndices);
    }

    [Fact]
    public void A_single_file_torrent_is_one_row()
    {
        var vm = Create();

        vm.Apply("h", [MakeFile(0, "ubuntu.iso", done: 100)]);

        var row = Assert.Single(vm.Rows);
        Assert.Equal("ubuntu.iso", row.Name);
        Assert.Equal(100, row.Progress);
    }

    [Fact]
    public void Unticking_a_file_tells_the_server_and_updates_its_folder()
    {
        var vm = Create();
        vm.Apply("h", Files);
        Row(vm, "Screens").IsExpanded = true;

        Row(vm, "s2.jpg").IsWanted = false;

        _actions.Received(1).SetFilesWantedAsync("h", Arg.Is<IReadOnlyList<int>>(i => i.SequenceEqual(new[] { 3 })), false, Arg.Any<CancellationToken>());
        Assert.False(Row(vm, "Screens").IsWanted);
    }

    [Fact]
    public void Clicking_a_mixed_folder_downloads_all_of_it()
    {
        var vm = Create();
        vm.Apply("h", Files);
        var screens = Row(vm, "Screens");

        screens.IsWanted = false; // what a two-state checkbox sends after "mixed"

        _actions.Received(1).SetFilesWantedAsync("h", Arg.Is<IReadOnlyList<int>>(i => i.OrderBy(x => x).SequenceEqual(new[] { 1, 3 })), true, Arg.Any<CancellationToken>());
        Assert.True(screens.IsWanted);
    }

    [Fact]
    public void Setting_a_folder_priority_applies_to_all_its_files()
    {
        var vm = Create();
        vm.Apply("h", Files);
        var screens = Row(vm, "Screens");

        screens.SetPriorityCommand.Execute(PriorityOption.High);

        _actions.Received(1).SetFilePriorityAsync("h", Arg.Is<IReadOnlyList<int>>(i => i.Count == 2), FilePriority.High, Arg.Any<CancellationToken>());
        Assert.Equal("High", screens.PriorityText);
    }

    [Fact]
    public void Choosing_the_current_value_sends_nothing()
    {
        var vm = Create();
        vm.Apply("h", Files);

        Row(vm, "a.mkv").IsWanted = true;
        Row(vm, "a.mkv").SetPriorityCommand.Execute(PriorityOption.High);

        Assert.Empty(_actions.ReceivedCalls());
    }

    [Fact]
    public void Poll_updates_keep_the_tree_and_its_expanded_folders()
    {
        var vm = Create();
        vm.Apply("h", Files);
        var screens = Row(vm, "Screens");
        screens.IsExpanded = true;

        vm.Apply("h", Files.Select(f => f with { BytesCompleted = f.Length }).ToList());

        Assert.Same(screens, Row(vm, "Screens"));
        Assert.True(screens.IsExpanded);
        Assert.Equal(100, screens.Progress);
    }

    [Fact]
    public void A_failed_change_is_explained()
    {
        _actions.SetFilesWantedAsync(default!, default!, default, default).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("boom")));
        var vm = Create();
        vm.Apply("h", Files);

        Row(vm, "a.mkv").IsWanted = false;

        _dialogs.Received(1).ShowMessageAsync("Couldn't change which files to download", "boom", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Choosing_mode_keeps_changes_local_and_reports_them()
    {
        var vm = FilesViewModel.ForChoosing();
        vm.Apply("new", Files);

        Row(vm, "b.nfo").IsWanted = false;
        Row(vm, "Screens").SetPriorityCommand.Execute(PriorityOption.Low);

        var choices = vm.GetChoices();
        Assert.Equal(new[] { 0, 1 }, choices.Unwanted); // b.nfo, plus s10.jpg which started unticked
        Assert.Equal(new[] { 2 }, choices.HighPriority);
        Assert.Equal(new[] { 1, 3 }, choices.LowPriority);
        Assert.Equal(2, vm.WantedCount); // a.mkv and s2.jpg
        Assert.StartsWith("2 of 4 files", vm.Summary);
    }

    [Fact]
    public void Renaming_a_folder_sends_its_full_path_on_the_server()
    {
        var vm = Create();
        vm.Apply("h", Files);
        _dialogs.PromptAsync(Arg.Any<TextPrompt>()).Returns("Screenshots");

        Row(vm, "Screens").RenameCommand.Execute(null);

        _actions.Received(1).RenamePathAsync("h", "T/Screens", "Screenshots");
    }

    [Fact]
    public void Renaming_a_file_sends_its_path_and_offers_its_name()
    {
        var vm = Create();
        vm.Apply("h", Files);
        _dialogs.PromptAsync(Arg.Any<TextPrompt>()).Returns("movie.mkv");

        Row(vm, "a.mkv").RenameCommand.Execute(null);

        _dialogs.Received(1).PromptAsync(Arg.Is<TextPrompt>(p => p.InitialText == "a.mkv" && p.Title == "Rename file"));
        _actions.Received(1).RenamePathAsync("h", "T/a.mkv", "movie.mkv");
    }

    [Fact]
    public void A_single_file_torrent_renames_by_its_file_name()
    {
        var vm = Create();
        vm.Apply("h", [MakeFile(0, "ubuntu.iso")]);
        _dialogs.PromptAsync(Arg.Any<TextPrompt>()).Returns("ubuntu-24.04.iso");

        Row(vm, "ubuntu.iso").RenameCommand.Execute(null);

        _actions.Received(1).RenamePathAsync("h", "ubuntu.iso", "ubuntu-24.04.iso");
    }

    [Fact]
    public void A_cancelled_rename_sends_nothing()
    {
        var vm = Create();
        vm.Apply("h", Files);
        _dialogs.PromptAsync(Arg.Any<TextPrompt>()).Returns((string?)null);

        Row(vm, "a.mkv").RenameCommand.Execute(null);

        _actions.DidNotReceiveWithAnyArgs().RenamePathAsync(default!, default!, default!);
    }

    [Fact]
    public void Files_chosen_before_adding_cannot_be_renamed()
    {
        var vm = FilesViewModel.ForChoosing();
        vm.Apply("", Files);

        Assert.False(vm.CanRename);
        Assert.False(Row(vm, "a.mkv").CanRename);
    }
}
