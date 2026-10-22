using NSubstitute;
using Synchro.AppServices.Connection;
using Synchro.Core.Servers;
using Synchro.Core.Settings;
using Synchro.ViewModels.Services;
using Synchro.ViewModels.Settings;
using Xunit;

namespace Synchro.Tests;

public class ServersViewModelTests
{
    private readonly IServerProfileStore _profiles = Substitute.For<IServerProfileStore>();
    private readonly ICredentialStore _credentials = Substitute.For<ICredentialStore>();
    private readonly IConnectionService _connection = Substitute.For<IConnectionService>();
    private readonly IAppSettingsStore _settings = Substitute.For<IAppSettingsStore>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly List<ServerProfile> _saved = [];
    private AppSettings _current = new();

    private static readonly ServerProfile Nas = new(Guid.NewGuid(), "NAS", "nas.local");
    private static readonly ServerProfile Seedbox = new(Guid.NewGuid(), "Seedbox", "seedbox.example", 443, UseHttps: true);

    public ServersViewModelTests()
    {
        _profiles.LoadAsync(Arg.Any<CancellationToken>()).Returns(_ => _saved.ToList());
        _profiles.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => { _saved.RemoveAll(p => p.Id == call.Arg<Guid>()); return Task.CompletedTask; });
        _settings.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>(), Arg.Any<CancellationToken>())
            .Returns(call => _current = call.Arg<Func<AppSettings, AppSettings>>()(_current));
        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Disconnected));
    }

    private ServersViewModel Create() => new(_profiles, _credentials, _connection, _settings, _dialogs);

    private void ConnectedTo(ServerProfile profile)
    {
        _connection.ActiveProfile.Returns(profile);
        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connected, "4.1.3"));
        _connection.StatusChanged += Raise.Event();
    }

    [Fact]
    public async Task Lists_the_servers_by_name_with_the_connected_one_marked()
    {
        _saved.AddRange([Seedbox, Nas]);
        var vm = Create();
        ConnectedTo(Seedbox);

        await vm.LoadAsync();

        Assert.Equal(new[] { "NAS", "Seedbox" }, vm.Servers.Select(s => s.Name));
        Assert.Equal(new[] { false, true }, vm.Servers.Select(s => s.IsActive));
        Assert.Equal("https://seedbox.example:443", vm.Servers[1].Address);
        Assert.Equal("Seedbox", vm.ActiveServerName);
    }

    [Fact]
    public async Task Connecting_switches_server_and_remembers_it()
    {
        _saved.AddRange([Nas, Seedbox]);
        var vm = Create();
        ConnectedTo(Nas);
        await vm.LoadAsync();

        await vm.ConnectCommand.ExecuteAsync(vm.Servers.Single(s => s.Name == "Seedbox"));

        await _connection.Received(1).ConnectAsync(Seedbox, Arg.Any<CancellationToken>());
        Assert.Equal(Seedbox.Id, _current.LastServerId);
    }

    [Fact]
    public async Task Connecting_to_the_server_already_connected_does_nothing()
    {
        _saved.Add(Nas);
        var vm = Create();
        ConnectedTo(Nas);
        await vm.LoadAsync();

        await vm.ConnectCommand.ExecuteAsync(vm.Servers[0]);

        await _connection.DidNotReceiveWithAnyArgs().ConnectAsync(default!, default);
    }

    [Fact]
    public async Task Adding_the_first_server_connects_to_it()
    {
        var vm = Create();
        _dialogs.ShowServerSetupAsync(null, Arg.Any<CancellationToken>())
            .Returns(_ => { _saved.Add(Nas); return Nas; });

        await vm.AddCommand.ExecuteAsync(null);

        Assert.Equal("NAS", vm.Selected?.Name);
        await _connection.Received(1).ConnectAsync(Nas, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Adding_another_server_keeps_the_current_connection()
    {
        _saved.Add(Nas);
        var vm = Create();
        ConnectedTo(Nas);
        await vm.LoadAsync();
        _dialogs.ShowServerSetupAsync(null, Arg.Any<CancellationToken>())
            .Returns(_ => { _saved.Add(Seedbox); return Seedbox; });

        await vm.AddCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Servers.Count);
        await _connection.DidNotReceiveWithAnyArgs().ConnectAsync(default!, default);
    }

    [Fact]
    public async Task Editing_the_connected_server_reconnects_with_the_new_details()
    {
        var moved = Nas with { Port = 9092 };
        _saved.Add(Nas);
        var vm = Create();
        ConnectedTo(Nas);
        await vm.LoadAsync();
        _dialogs.ShowServerSetupAsync(Nas, Arg.Any<CancellationToken>())
            .Returns(_ => { _saved[0] = moved; return moved; });

        await vm.EditCommand.ExecuteAsync(vm.Servers[0]);

        await _connection.Received(1).ConnectAsync(moved, Arg.Any<CancellationToken>());
        Assert.Equal("nas.local:9092", vm.Servers[0].Address);
    }

    [Fact]
    public async Task Removing_asks_first_and_deletes_the_password_too()
    {
        _saved.AddRange([Nas, Seedbox]);
        var vm = Create();
        ConnectedTo(Nas);
        await vm.LoadAsync();
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), "Remove", Arg.Any<CancellationToken>()).Returns(true);

        await vm.RemoveCommand.ExecuteAsync(vm.Servers.Single(s => s.Name == "Seedbox"));

        await _credentials.Received(1).DeleteAsync(Seedbox.Id, Arg.Any<CancellationToken>());
        Assert.Equal(new[] { "NAS" }, vm.Servers.Select(s => s.Name));
        await _connection.DidNotReceiveWithAnyArgs().ConnectAsync(default!, default);
    }

    [Fact]
    public async Task Declining_the_question_keeps_the_server()
    {
        _saved.Add(Nas);
        var vm = Create();
        await vm.LoadAsync();
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        await vm.RemoveCommand.ExecuteAsync(vm.Servers[0]);

        await _profiles.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
        Assert.Single(vm.Servers);
    }

    [Fact]
    public async Task Removing_the_connected_server_moves_to_another_one()
    {
        _saved.AddRange([Nas, Seedbox]);
        var vm = Create();
        ConnectedTo(Nas);
        await vm.LoadAsync();
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        await vm.RemoveCommand.ExecuteAsync(vm.Servers.Single(s => s.Name == "NAS"));

        await _connection.Received(1).ConnectAsync(Seedbox, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Removing_the_last_server_disconnects()
    {
        _saved.Add(Nas);
        var vm = Create();
        ConnectedTo(Nas);
        await vm.LoadAsync();
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        await vm.RemoveCommand.ExecuteAsync(vm.Servers[0]);

        await _connection.Received(1).DisconnectAsync();
        Assert.False(vm.HasServers);
    }

    [Fact]
    public async Task The_server_in_use_shows_its_real_connection_state()
    {
        _saved.AddRange([Nas, Seedbox]);
        var vm = Create();
        await vm.LoadAsync();
        var nas = vm.Servers.Single(s => s.Name == "NAS");

        _connection.ActiveProfile.Returns(Nas);
        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Connecting));
        _connection.StatusChanged += Raise.Event();
        Assert.Equal("Connecting…", nas.StateText);
        Assert.True(nas.IsConnecting);

        _connection.Status.Returns(new ConnectionStatus(ConnectionState.Failed, Message: "The connection timed out."));
        _connection.StatusChanged += Raise.Event();
        Assert.Equal("Can't connect", nas.StateText);
        Assert.True(nas.IsFailed);
        Assert.False(nas.IsConnected);
        Assert.Equal("The connection timed out.", nas.StateDetail);

        Assert.Equal("", vm.Servers.Single(s => s.Name == "Seedbox").StateText);
    }
}
