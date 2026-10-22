using Synchro.Core.Torrents;
using Synchro.ViewModels;
using Synchro.ViewModels.Services;
using Xunit;

namespace Synchro.Tests;

public class ConfirmRemoveViewModelTests
{
    [Fact]
    public void Names_the_torrent_when_there_is_one_and_counts_when_there_are_several()
    {
        Assert.Contains("Ubuntu", new ConfirmRemoveViewModel(["Ubuntu"]).Message);
        Assert.Contains("3 torrents", new ConfirmRemoveViewModel(["a", "b", "c"]).Message);
    }

    [Fact]
    public void Keeps_the_files_unless_asked_otherwise()
    {
        var vm = new ConfirmRemoveViewModel(["a"]);
        RemoveRequest? result = null;
        vm.CloseRequested += (_, r) => result = r;

        vm.ConfirmCommand.Execute(null);

        Assert.False(Assert.IsType<RemoveRequest>(result).DeleteLocalData);
    }

    [Fact]
    public void Deletes_the_files_when_the_box_is_ticked()
    {
        var vm = new ConfirmRemoveViewModel(["a"]) { DeleteLocalData = true };
        RemoveRequest? result = null;
        vm.CloseRequested += (_, r) => result = r;

        vm.ConfirmCommand.Execute(null);

        Assert.True(Assert.IsType<RemoveRequest>(result).DeleteLocalData);
    }

    [Fact]
    public void Cancel_closes_with_null()
    {
        var vm = new ConfirmRemoveViewModel(["a"]);
        RemoveRequest? result = new(true);
        vm.CloseRequested += (_, r) => result = r;

        vm.CancelCommand.Execute(null);

        Assert.Null(result);
    }
}

public class SpeedLimitsViewModelTests
{
    private static readonly SpeedLimits Current = new(true, 1500, false, 200, true);

    private static async Task<SpeedLimitsViewModel> Loaded(SpeedLimits limits)
    {
        var vm = new SpeedLimitsViewModel(_ => Task.FromResult(limits));
        await vm.LoadAsync();
        return vm;
    }

    [Fact]
    public async Task Starts_from_the_servers_current_limits()
    {
        var vm = await Loaded(Current);

        Assert.True(vm.DownloadLimited);
        Assert.Equal(1500, vm.DownloadLimit);
        Assert.False(vm.UploadLimited);
        Assert.Equal(200, vm.UploadLimit);
        Assert.True(vm.AlternativeSpeedsEnabled);
        Assert.True(vm.IsReady);
    }

    [Fact]
    public async Task Save_waits_until_the_limits_are_loaded()
    {
        var pending = new TaskCompletionSource<SpeedLimits>();
        var vm = new SpeedLimitsViewModel(_ => pending.Task);
        var load = vm.LoadAsync();

        Assert.True(vm.IsLoading);
        Assert.False(vm.SaveCommand.CanExecute(null));

        pending.SetResult(Current);
        await load;

        Assert.False(vm.IsLoading);
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_failed_load_is_explained_and_only_cancel_remains()
    {
        var vm = new SpeedLimitsViewModel(_ => Task.FromException<SpeedLimits>(new HttpRequestException("offline")));

        await vm.LoadAsync();

        Assert.Equal("Couldn't read the server's speed limits: offline", vm.LoadError);
        Assert.False(vm.IsReady);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.True(vm.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task Save_returns_what_was_entered()
    {
        var vm = await Loaded(Current);
        vm.UploadLimited = true;
        vm.UploadLimit = 75;
        SpeedLimits? result = null;
        vm.CloseRequested += (_, l) => result = l;

        vm.SaveCommand.Execute(null);

        Assert.Equal(new SpeedLimits(true, 1500, true, 75, true), result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Rejects_a_limit_below_one(int value)
    {
        var vm = await Loaded(Current);
        SpeedLimits? result = null;
        vm.CloseRequested += (_, l) => result = l;

        vm.DownloadLimit = value;

        Assert.True(vm.HasErrors);
        Assert.False(vm.SaveCommand.CanExecute(null));
        vm.SaveCommand.Execute(null);
        Assert.Null(result);
    }

    [Fact]
    public async Task Shows_a_usable_value_when_the_server_reports_zero()
    {
        var vm = await Loaded(new SpeedLimits(false, 0, false, 0, false));

        Assert.True(vm.DownloadLimit >= 1);
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void Cancel_closes_with_null()
    {
        var vm = new SpeedLimitsViewModel(_ => Task.FromResult(Current));
        SpeedLimits? result = Current;
        vm.CloseRequested += (_, l) => result = l;

        vm.CancelCommand.Execute(null);

        Assert.Null(result);
    }
}
