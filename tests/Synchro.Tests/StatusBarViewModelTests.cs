using NSubstitute;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Servers;
using Synchro.Core.Torrents;
using Synchro.ViewModels;
using Xunit;

namespace Synchro.Tests;

public class StatusBarViewModelTests
{
    private readonly IConnectionService _connection = Substitute.For<IConnectionService>();
    private readonly ITorrentFeed _feed = Substitute.For<ITorrentFeed>();

    private void SetState(ConnectionStatus status)
    {
        _connection.Status.Returns(status);
        _connection.StatusChanged += Raise.Event<EventHandler>(_connection, EventArgs.Empty);
    }

    [Fact]
    public void Shows_speeds_and_torrent_count_while_connected()
    {
        _connection.ActiveProfile.Returns(new ServerProfile(Guid.NewGuid(), "NAS", "nas"));
        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connected, "4.1.0"));
        var vm = new StatusBarViewModel(_connection, _feed);

        _feed.StatsUpdated += Raise.Event<EventHandler<SessionStats>>(_feed, new SessionStats(12, 3, 2, 4_300_000, 200_000));

        Assert.Contains("MB/s", vm.DownloadSpeedText);
        Assert.StartsWith("↓", vm.DownloadSpeedText);
        Assert.StartsWith("↑", vm.UploadSpeedText);
        Assert.Equal("12 torrents", vm.CountText);
        Assert.Contains("NAS", vm.Text);
    }

    [Fact]
    public void Uses_the_singular_for_one_torrent()
    {
        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connected, "4.1.0"));
        var vm = new StatusBarViewModel(_connection, _feed);

        _feed.StatsUpdated += Raise.Event<EventHandler<SessionStats>>(_feed, new SessionStats(1, 0, 0, 0, 0));

        Assert.Equal("1 torrent", vm.CountText);
    }

    [Fact]
    public void Clears_the_numbers_when_the_connection_is_lost()
    {
        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connected, "4.1.0"));
        var vm = new StatusBarViewModel(_connection, _feed);
        _feed.StatsUpdated += Raise.Event<EventHandler<SessionStats>>(_feed, new SessionStats(5, 1, 0, 1_000_000, 0));

        SetState(new ConnectionStatus(ConnectionState.Failed, Message: "Connection lost"));

        Assert.Equal("", vm.DownloadSpeedText);
        Assert.Equal("", vm.CountText);
        Assert.True(vm.HasError);
    }

    [Fact]
    public void Warns_while_connected_to_an_untested_rpc_version()
    {
        _connection.ActiveProfile.Returns(new ServerProfile(Guid.NewGuid(), "NAS", "nas"));
        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Disconnected));
        var vm = new StatusBarViewModel(_connection, _feed);
        var untested = new ServerCapabilities(new Version(7, 0, 0));

        SetState(new ConnectionStatus(ConnectionState.Connected, "5.0.0", Capabilities: untested));
        Assert.True(vm.HasWarning);
        Assert.Contains("7.0.0 is newer than this app was tested with", vm.Text);

        SetState(new ConnectionStatus(ConnectionState.Failed, Message: "Connection lost", Capabilities: untested));
        Assert.False(vm.HasWarning);
    }

    [Fact]
    public void No_warning_for_a_tested_rpc_version()
    {
        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connected, "4.1.3", Capabilities: ServerCapabilities.Baseline));

        var vm = new StatusBarViewModel(_connection, _feed);

        Assert.False(vm.HasWarning);
        Assert.EndsWith("(Transmission 4.1.3)", vm.Text);
    }
}
