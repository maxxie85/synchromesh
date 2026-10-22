using NSubstitute;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Servers;
using Synchro.Core.Torrents;
using Xunit;

namespace Synchro.Tests;

public class ConnectionServiceTests
{
    private readonly IConnectionTester _tester = Substitute.For<IConnectionTester>();
    private readonly ICredentialStore _credentials = Substitute.For<ICredentialStore>();
    private readonly ITransmissionClientFactory _clients = Substitute.For<ITransmissionClientFactory>();
    private readonly ITransmissionClient _client = Substitute.For<ITransmissionClient>();
    private readonly ITorrentPoller _poller = Substitute.For<ITorrentPoller>();
    private readonly ServerProfile _profile = new(Guid.NewGuid(), "NAS", "nas.local", Username: "admin");

    private ConnectionService Create() => new(_tester, _credentials, _clients, _poller);

    private static ServerCapabilities Capabilities(string rpc)
    {
        RpcVersion.TryParse(rpc, out var version);
        return new ServerCapabilities(version);
    }

    private void TesterSucceeds(string version = "4.1.0", string rpc = "6.0.0")
    {
        _credentials.GetPasswordAsync(_profile.Id, Arg.Any<CancellationToken>()).Returns("secret");
        _tester.TestAsync(_profile, "secret", Arg.Any<CancellationToken>())
            .Returns(new ConnectionTestResult.Success(version, Capabilities(rpc)));
        _clients.Create(_profile, "secret", Arg.Any<ServerCapabilities>()).Returns(_client);
    }

    [Fact]
    public async Task Connects_using_the_stored_password_and_starts_polling()
    {
        TesterSucceeds();
        var states = new List<ConnectionState>();
        var service = Create();
        service.StatusChanged += (_, _) => states.Add(service.Status.State);

        await service.ConnectAsync(_profile);

        Assert.Equal([ConnectionState.Connecting, ConnectionState.Connected], states);
        Assert.Equal("4.1.0", service.Status.ServerVersion);
        Assert.Equal(_profile, service.ActiveProfile);
        _poller.Received(1).Start(_client);
    }

    [Fact]
    public async Task Stops_the_previous_polling_before_connecting_again()
    {
        TesterSucceeds();

        await Create().ConnectAsync(_profile);

        await _poller.Received(1).StopAsync();
    }

    [Fact]
    public async Task Reports_failure_messages_and_does_not_poll()
    {
        _tester.TestAsync(Arg.Any<ServerProfile>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ConnectionTestResult.Failure(ConnectionFailure.Unreachable, "nope"));
        var service = Create();

        await service.ConnectAsync(_profile);

        Assert.Equal(ConnectionState.Failed, service.Status.State);
        Assert.Equal("nope", service.Status.Message);
        _poller.DidNotReceiveWithAnyArgs().Start(default!);
    }

    [Fact]
    public async Task Fails_gracefully_when_the_keychain_throws()
    {
        _credentials.GetPasswordAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<Task<string?>>(_ => throw new InvalidOperationException("locked"));
        var service = Create();

        await service.ConnectAsync(_profile);

        Assert.Equal(ConnectionState.Failed, service.Status.State);
        Assert.Contains("locked", service.Status.Message);
        await _tester.DidNotReceiveWithAnyArgs().TestAsync(default!, default, default);
    }

    [Fact]
    public async Task Shows_a_lost_connection_and_recovers_when_polling_resumes()
    {
        TesterSucceeds("4.2.0");
        var service = Create();
        await service.ConnectAsync(_profile);

        _poller.ConnectionLost += Raise.Event<EventHandler<string>>(_poller, "timed out");
        Assert.Equal(ConnectionState.Failed, service.Status.State);
        Assert.Contains("timed out", service.Status.Message);

        _poller.ConnectionRestored += Raise.Event<EventHandler>(_poller, EventArgs.Empty);
        Assert.Equal(ConnectionState.Connected, service.Status.State);
        Assert.Equal("4.2.0", service.Status.ServerVersion);
    }

    [Fact]
    public async Task Exposes_the_client_of_the_active_server()
    {
        TesterSucceeds();
        var service = Create();
        Assert.Null(service.Client);

        await service.ConnectAsync(_profile);

        Assert.Same(_client, service.Client);
    }

    [Fact]
    public async Task Disconnecting_stops_polling_and_forgets_the_server()
    {
        _tester.TestAsync(_profile, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ConnectionTestResult.Success("4.1.3", ServerCapabilities.Baseline));
        var service = Create();
        await service.ConnectAsync(_profile);

        await service.DisconnectAsync();

        await _poller.Received().StopAsync();
        Assert.Null(service.ActiveProfile);
        Assert.Null(service.Client);
        Assert.Equal(ConnectionState.Disconnected, service.Status.State);
    }

    [Fact]
    public async Task The_servers_rpc_version_reaches_the_client_and_the_status()
    {
        TesterSucceeds(rpc: "6.1.0");
        var service = Create();

        await service.ConnectAsync(_profile);

        _clients.Received(1).Create(_profile, "secret", Capabilities("6.1.0"));
        Assert.Equal(Capabilities("6.1.0"), service.Status.Capabilities);

        _poller.ConnectionLost += Raise.Event<EventHandler<string>>(_poller, "timed out");
        _poller.ConnectionRestored += Raise.Event<EventHandler>(_poller, EventArgs.Empty);
        Assert.Equal(Capabilities("6.1.0"), service.Status.Capabilities);
    }
}
