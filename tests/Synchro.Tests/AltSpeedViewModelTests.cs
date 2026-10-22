using NSubstitute;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.ViewModels;
using Synchro.ViewModels.Services;
using Xunit;

namespace Synchro.Tests;

public sealed class AltSpeedViewModelTests : IDisposable
{
    private readonly ISpeedLimitService _speedLimits = Substitute.For<ISpeedLimitService>();
    private readonly IConnectionService _connection = Substitute.For<IConnectionService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly List<AltSpeedViewModel> _created = [];
    private AltSpeed _server = new(false, 50, 20);

    public AltSpeedViewModelTests()
    {
        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Disconnected));
        _speedLimits.GetAltSpeedAsync(Arg.Any<CancellationToken>()).Returns(_ => _server);
        _speedLimits.SetAltSpeedEnabledAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call => { _server = _server with { Enabled = call.Arg<bool>() }; return Task.CompletedTask; });
    }

    public void Dispose() => _created.ForEach(vm => vm.Dispose());

    private AltSpeedViewModel Create()
    {
        var vm = new AltSpeedViewModel(_speedLimits, _connection, _dialogs, TimeProvider.System);
        _created.Add(vm);
        return vm;
    }

    [Fact]
    public async Task Shows_the_servers_state_and_limits()
    {
        _server = new AltSpeed(true, 50, 20);
        var vm = Create();

        await vm.RefreshAsync();

        Assert.True(vm.IsAvailable);
        Assert.True(vm.IsEnabled);
        Assert.Contains("on", vm.ToolTip);
        Assert.Contains("50 kB/s", vm.ToolTip);
    }

    [Fact]
    public async Task Toggling_switches_the_server_and_rereads_it()
    {
        var vm = Create();
        await vm.RefreshAsync();

        await vm.ToggleCommand.ExecuteAsync(null);

        await _speedLimits.Received(1).SetAltSpeedEnabledAsync(true, Arg.Any<CancellationToken>());
        Assert.True(vm.IsEnabled);
    }

    [Fact]
    public async Task A_failed_toggle_is_explained_and_the_real_state_shown()
    {
        _speedLimits.SetAltSpeedEnabledAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));
        var vm = Create();
        await vm.RefreshAsync();

        await vm.ToggleCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ShowMessageAsync("Couldn't change the speed limits", "boom", Arg.Any<CancellationToken>());
        Assert.False(vm.IsEnabled);
    }

    [Fact]
    public async Task It_follows_the_connection_and_picks_up_schedule_changes()
    {
        var vm = Create();
        vm.RefreshInterval = TimeSpan.FromMilliseconds(10);

        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connected));
        _connection.StatusChanged += Raise.Event();
        await TestWait.UntilAsync(() => vm.IsAvailable);

        _server = _server with { Enabled = true }; // the server's own schedule switched them on
        await TestWait.UntilAsync(() => vm.IsEnabled);

        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connecting));
        _connection.StatusChanged += Raise.Event();
        Assert.False(vm.IsAvailable);
    }
}
