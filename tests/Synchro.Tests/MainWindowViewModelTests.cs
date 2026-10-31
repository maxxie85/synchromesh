using NSubstitute;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Servers;
using Synchro.Core.Settings;
using Synchro.ViewModels;
using Synchro.ViewModels.Settings;
using Synchro.ViewModels.Details;
using Synchro.ViewModels.Services;
using Xunit;

namespace Synchro.Tests;

public class MainWindowViewModelTests
{
    private readonly IServerProfileStore _profiles = Substitute.For<IServerProfileStore>();
    private readonly IConnectionService _connection = Substitute.For<IConnectionService>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IExternalAddQueue _externalAdds = Substitute.For<IExternalAddQueue>();
    private readonly IAppSettingsStore _settings = Substitute.For<IAppSettingsStore>();
    private readonly ServerProfile _profile = new(Guid.NewGuid(), "NAS", "nas.local");

    public MainWindowViewModelTests()
    {
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings());
        _settings.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<AppSettings, AppSettings>>()(new AppSettings()));
    }

    private MainWindowViewModel Create()
    {
        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Disconnected));
        var feed = Substitute.For<ITorrentFeed>();
        var list = new TorrentListViewModel(feed, _connection);
        var toolbar = new ToolbarViewModel(list, _connection, Substitute.For<ITorrentActions>(),
            Substitute.For<ISpeedLimitService>(), _dialogs, Substitute.For<IAddTorrentFlow>());
        var servers = new ServersViewModel(_profiles, Substitute.For<ICredentialStore>(), _connection, _settings, _dialogs);
        return new MainWindowViewModel(_profiles, _settings, _dialogs, _externalAdds,
            new StatusBarViewModel(_connection, feed), list, toolbar, new HelpViewModel(_dialogs, Substitute.For<IUrlLauncher>(),
                Substitute.For<IStallLogging>(), _settings),
            new TorrentDetailsViewModel(Substitute.For<ITorrentDetailsFeed>(), list, TimeProvider.System,
                Substitute.For<ITorrentActions>(), _dialogs),
            new StatisticsViewModel(feed),
            new SpeedHistoryViewModel(feed, _connection, TimeProvider.System),
            new StorageViewModel(Substitute.For<IStorageService>(), _connection, TimeProvider.System),
            new AltSpeedViewModel(Substitute.For<ISpeedLimitService>(), _connection, _dialogs, TimeProvider.System), servers);
    }

    [Fact]
    public async Task First_run_opens_setup_and_connects_to_the_new_server()
    {
        _profiles.LoadAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ServerProfile>());
        _dialogs.ShowServerSetupAsync(null, Arg.Any<CancellationToken>()).Returns(_profile);

        await Create().InitializeCommand.ExecuteAsync(null);

        await _connection.Received(1).ConnectAsync(_profile, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task First_run_cancel_does_not_connect()
    {
        _profiles.LoadAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ServerProfile>());
        _dialogs.ShowServerSetupAsync(null, Arg.Any<CancellationToken>()).Returns((ServerProfile?)null);

        await Create().InitializeCommand.ExecuteAsync(null);

        await _connection.DidNotReceiveWithAnyArgs().ConnectAsync(default!, default);
    }

    [Fact]
    public async Task With_a_saved_server_it_connects_without_showing_setup()
    {
        _profiles.LoadAsync(Arg.Any<CancellationToken>()).Returns(new[] { _profile });

        await Create().InitializeCommand.ExecuteAsync(null);

        await _dialogs.DidNotReceiveWithAnyArgs().ShowServerSetupAsync(default, default);
        await _connection.Received(1).ConnectAsync(_profile, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task On_start_it_reconnects_to_the_server_used_last()
    {
        var other = new ServerProfile(Guid.NewGuid(), "Seedbox", "seedbox.example");
        _profiles.LoadAsync(Arg.Any<CancellationToken>()).Returns(new[] { _profile, other });
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings(LastServerId: other.Id));

        await Create().InitializeCommand.ExecuteAsync(null);

        await _connection.Received(1).ConnectAsync(other, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_last_server_that_was_removed_falls_back_to_the_first()
    {
        _profiles.LoadAsync(Arg.Any<CancellationToken>()).Returns(new[] { _profile });
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings(LastServerId: Guid.NewGuid()));

        await Create().InitializeCommand.ExecuteAsync(null);

        await _connection.Received(1).ConnectAsync(_profile, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Settings_open_on_the_servers_section()
    {
        await Create().OpenSettingsCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ShowSettingsAsync(SettingsSection.Servers, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Opened_torrents_are_released_only_after_the_first_connection_attempt()
    {
        _profiles.LoadAsync(Arg.Any<CancellationToken>()).Returns(new[] { _profile });

        await Create().InitializeCommand.ExecuteAsync(null);

        Received.InOrder(() =>
        {
            _connection.ConnectAsync(_profile, Arg.Any<CancellationToken>());
            _externalAdds.Start();
        });
    }

    [Fact]
    public async Task Opened_torrents_are_released_even_when_setup_is_cancelled()
    {
        _profiles.LoadAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ServerProfile>());
        _dialogs.ShowServerSetupAsync(null, Arg.Any<CancellationToken>()).Returns((ServerProfile?)null);

        await Create().InitializeCommand.ExecuteAsync(null);

        _externalAdds.Received(1).Start();
    }

    [Fact]
    public async Task The_list_sort_is_restored_on_start_and_remembered()
    {
        var saved = new AppSettings(ListSort: new Synchro.Core.Settings.TorrentSort(Synchro.Core.Settings.TorrentSortColumn.Name, true));
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(saved);
        _settings.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>(), Arg.Any<CancellationToken>())
            .Returns(call => saved = call.Arg<Func<AppSettings, AppSettings>>()(saved));
        _profiles.LoadAsync(Arg.Any<CancellationToken>()).Returns(new[] { _profile });
        var vm = Create();

        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.Equal(new Synchro.Core.Settings.TorrentSort(Synchro.Core.Settings.TorrentSortColumn.Name, true), vm.TorrentList.Sort);

        vm.TorrentList.SortByCommand.Execute(Synchro.Core.Settings.TorrentSortColumn.Size);
        Assert.Equal(new Synchro.Core.Settings.TorrentSort(Synchro.Core.Settings.TorrentSortColumn.Size, true), saved.ListSort);

        vm.TorrentList.Sort = Synchro.Core.Settings.TorrentSort.Default;
        Assert.Null(saved.ListSort); // queue order is the default, nothing to remember
    }
}
