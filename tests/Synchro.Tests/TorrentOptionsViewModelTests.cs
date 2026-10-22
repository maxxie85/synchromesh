using NSubstitute;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.ViewModels;
using Synchro.ViewModels.Details;
using Synchro.ViewModels.Services;
using Xunit;

namespace Synchro.Tests;

public class TorrentOptionsViewModelTests
{
    private readonly ITorrentActions _actions = Substitute.For<ITorrentActions>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    private static readonly TorrentOptions Server = new(false, 100, true, 250, true, BandwidthPriority.Normal,
        SeedLimitMode.ServerDefault, 2.0, SeedLimitMode.ServerDefault, 30, 60);

    private TorrentOptionsViewModel Create() => new(_actions, _dialogs);

    [Fact]
    public void Loading_fills_the_form_without_marking_it_changed()
    {
        var vm = Create();

        vm.Load("abc", Server);

        Assert.True(vm.HasOptions);
        Assert.True(vm.UploadLimited);
        Assert.Equal(250, vm.UploadLimit);
        Assert.Equal(BandwidthPriority.Normal, vm.Priority?.Value);
        Assert.False(vm.IsDirty);
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public void An_edit_survives_the_next_poll()
    {
        var vm = Create();
        vm.Load("abc", Server);

        vm.UploadLimit = 500;
        vm.Load("abc", Server); // the next details poll, two seconds later

        Assert.Equal(500, vm.UploadLimit);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void Undoing_an_edit_by_hand_is_not_a_change()
    {
        var vm = Create();
        vm.Load("abc", Server);

        vm.UploadLimit = 500;
        vm.UploadLimit = 250;

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task Apply_sends_the_whole_form_for_that_torrent()
    {
        var vm = Create();
        vm.Load("abc", Server);
        vm.DownloadLimited = true;
        vm.DownloadLimit = 1000;
        vm.Priority = vm.Priorities.Single(p => p.Value == BandwidthPriority.High);
        vm.RatioMode = vm.RatioModes.Single(m => m.Value == SeedLimitMode.Custom);
        vm.RatioLimit = 1.5;
        vm.SequentialDownload = true;

        await vm.ApplyCommand.ExecuteAsync(null);

        await _actions.Received(1).SetOptionsAsync("abc", Server with
        {
            DownloadLimited = true, DownloadLimitKilobytes = 1000, Priority = BandwidthPriority.High,
            RatioMode = SeedLimitMode.Custom, RatioLimit = 1.5, SequentialDownload = true,
        }, Arg.Any<CancellationToken>());
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Revert_restores_the_servers_values()
    {
        var vm = Create();
        vm.Load("abc", Server);
        vm.PeerLimit = 5;

        vm.RevertCommand.Execute(null);

        Assert.Equal(60, vm.PeerLimit);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void An_invalid_number_blocks_apply()
    {
        var vm = Create();
        vm.Load("abc", Server);

        vm.PeerLimit = 0;

        Assert.True(vm.IsDirty);
        Assert.True(vm.HasErrors);
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public void Another_torrent_replaces_unapplied_edits()
    {
        var vm = Create();
        vm.Load("abc", Server);
        vm.UploadLimit = 999;

        vm.Clear();
        vm.Load("def", Server with { UploadLimitKilobytes = 10 });

        Assert.Equal(10, vm.UploadLimit);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task A_failed_apply_is_explained_and_keeps_the_edits()
    {
        _actions.SetOptionsAsync(default!, default!, default).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("boom")));
        var vm = Create();
        vm.Load("abc", Server);
        vm.PeerLimit = 100;

        await vm.ApplyCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ShowMessageAsync("Couldn't change the torrent's options", "boom", Arg.Any<CancellationToken>());
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void The_custom_limit_fields_show_only_for_custom_modes()
    {
        var vm = Create();
        vm.Load("abc", Server);
        Assert.False(vm.HasCustomRatio);

        vm.RatioMode = vm.RatioModes.Single(m => m.Value == SeedLimitMode.Custom);

        Assert.True(vm.HasCustomRatio);
        Assert.False(vm.HasCustomIdle);
    }
}

public class MoveDataViewModelTests
{
    [Fact]
    public void Starts_from_the_current_folder_and_moves_the_files()
    {
        var vm = new MoveDataViewModel(["Ubuntu"], "/data/downloads");
        MoveDataRequest? result = null;
        vm.CloseRequested += (_, r) => result = r;

        vm.Location = " /data/iso ";
        vm.MoveCommand.Execute(null);

        Assert.Contains("“Ubuntu”", vm.Message);
        Assert.Equal(new MoveDataRequest("/data/iso", MoveFiles: true), result);
    }

    [Fact]
    public void An_empty_folder_cannot_be_chosen()
    {
        var vm = new MoveDataViewModel(["a", "b"], "/data");

        vm.Location = "  ";

        Assert.False(vm.MoveCommand.CanExecute(null));
        Assert.Contains("2 torrents", vm.Message);
    }
}
