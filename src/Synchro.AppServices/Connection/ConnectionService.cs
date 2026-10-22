using Synchro.AppServices.Torrents;
using Synchro.Core.Servers;
using Synchro.Core.Torrents;

namespace Synchro.AppServices.Connection;

// No ConfigureAwait(false) on purpose: StatusChanged must be raised back on the caller's
// (UI) context so bound view models can update safely.
public sealed class ConnectionService : IConnectionService, IDisposable
{
    private readonly IConnectionTester _tester;
    private readonly ICredentialStore _credentials;
    private readonly ITransmissionClientFactory _clients;
    private readonly ITorrentPoller _poller;

    private ITransmissionClient? _client;
    private string? _serverVersion;
    private ServerCapabilities? _capabilities;

    public ConnectionService(
        IConnectionTester tester,
        ICredentialStore credentials,
        ITransmissionClientFactory clients,
        ITorrentPoller poller)
    {
        _tester = tester;
        _credentials = credentials;
        _clients = clients;
        _poller = poller;

        _poller.ConnectionLost += OnConnectionLost;
        _poller.ConnectionRestored += OnConnectionRestored;
    }

    public ConnectionStatus Status { get; private set; } = new(ConnectionState.Disconnected);
    public ServerProfile? ActiveProfile { get; private set; }
    public ITransmissionClient? Client => _client;
    public event EventHandler? StatusChanged;

    public async Task ConnectAsync(ServerProfile profile, CancellationToken ct = default)
    {
        await _poller.StopAsync();
        _client?.Dispose();
        _client = null;

        ActiveProfile = profile;
        SetStatus(new ConnectionStatus(ConnectionState.Connecting));

        string? password;
        try
        {
            password = await _credentials.GetPasswordAsync(profile.Id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus(new ConnectionStatus(ConnectionState.Failed, Message: $"Couldn't read the saved password: {ex.Message}"));
            return;
        }

        var result = await _tester.TestAsync(profile, password, ct);
        switch (result)
        {
            case ConnectionTestResult.Success success:
                _serverVersion = success.ServerVersion;
                _capabilities = success.Capabilities;
                try
                {
                    _client = _clients.Create(profile, password, success.Capabilities);
                }
                catch (Exception ex)
                {
                    // The test just read the same files; only a file changed in between gets here.
                    SetStatus(new ConnectionStatus(ConnectionState.Failed, Message: ex.Message));
                    return;
                }
                _poller.Start(_client);
                SetStatus(Connected());
                break;

            case ConnectionTestResult.Failure failure:
                SetStatus(new ConnectionStatus(ConnectionState.Failed, Message: failure.Message));
                break;

            default:
                SetStatus(new ConnectionStatus(ConnectionState.Failed, Message: "Unknown connection result."));
                break;
        }
    }

    public async Task DisconnectAsync()
    {
        await _poller.StopAsync();
        _client?.Dispose();
        _client = null;
        ActiveProfile = null;
        SetStatus(new ConnectionStatus(ConnectionState.Disconnected));
    }

    public void Dispose()
    {
        _poller.ConnectionLost -= OnConnectionLost;
        _poller.ConnectionRestored -= OnConnectionRestored;
        _client?.Dispose();
    }

    private void OnConnectionLost(object? sender, string reason)
        => SetStatus(new ConnectionStatus(ConnectionState.Failed, Message: $"Connection lost: {reason} Retrying…"));

    private void OnConnectionRestored(object? sender, EventArgs e)
        => SetStatus(Connected());

    private ConnectionStatus Connected() =>
        new(ConnectionState.Connected, ServerVersion: _serverVersion, Capabilities: _capabilities);

    private void SetStatus(ConnectionStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
