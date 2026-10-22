using Synchro.Core.Servers;
using Synchro.Core.Torrents;

namespace Synchro.AppServices.Connection;

public sealed class ServerPreferencesService(IConnectionService connection) : IServerPreferencesService
{
    public async Task<ServerPreferences?> GetAsync(CancellationToken ct = default)
        => connection.Client is { } client ? await client.GetServerPreferencesAsync(ct) : null;

    public Task SetAsync(ServerPreferences preferences, CancellationToken ct = default)
        => Client().SetServerPreferencesAsync(preferences, ct);

    public Task<int> GetBlocklistSizeAsync(CancellationToken ct = default) => Client().GetBlocklistSizeAsync(ct);

    public Task<int> UpdateBlocklistAsync(CancellationToken ct = default) => Client().UpdateBlocklistAsync(ct);

    public async Task<IReadOnlyList<PortTestResult>> TestPortAsync(CancellationToken ct = default)
    {
        var client = Client();
        var results = new List<PortTestResult>();
        foreach (var protocol in new[] { "ipv4", "ipv6" })
        {
            try
            {
                results.Add(new PortTestResult(protocol, await client.TestPortAsync(protocol, ct)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Typically no IPv6 connectivity on the server: report "couldn't test", not an error.
                results.Add(new PortTestResult(protocol, null));
            }
        }
        return results;
    }

    private ITransmissionClient Client()
        => connection.Client ?? throw new InvalidOperationException("Not connected to a server.");
}
