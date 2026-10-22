using Synchro.Core.Servers;

namespace Synchro.AppServices.Connection;

/// <summary>The connected server's own preferences, and its port check.</summary>
public interface IServerPreferencesService
{
    /// <summary>Null when not connected.</summary>
    Task<ServerPreferences?> GetAsync(CancellationToken ct = default);

    Task SetAsync(ServerPreferences preferences, CancellationToken ct = default);

    /// <summary>Rules in the server's blocklist now.</summary>
    Task<int> GetBlocklistSizeAsync(CancellationToken ct = default);

    /// <summary>Downloads the blocklist again from its saved address; returns the new number of rules.</summary>
    Task<int> UpdateBlocklistAsync(CancellationToken ct = default);

    /// <summary>Tests the peer port over IPv4 and IPv6; a version that can't be tested reports IsOpen = null.</summary>
    Task<IReadOnlyList<PortTestResult>> TestPortAsync(CancellationToken ct = default);
}
