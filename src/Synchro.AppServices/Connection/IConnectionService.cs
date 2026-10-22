using Synchro.Core.Servers;
using Synchro.Core.Torrents;

namespace Synchro.AppServices.Connection;

/// <summary>
/// Owns the connection to the active server and exposes its state.
/// </summary>
public interface IConnectionService
{
    ConnectionStatus Status { get; }
    ServerProfile? ActiveProfile { get; }

    /// <summary>The client for the active server, or null when there is none.</summary>
    ITransmissionClient? Client { get; }

    /// <summary>Raised on the thread that called <see cref="ConnectAsync"/> (the UI thread in practice).</summary>
    event EventHandler? StatusChanged;

    Task ConnectAsync(ServerProfile profile, CancellationToken ct = default);

    /// <summary>Stops polling and forgets the active server (for example after it was removed).</summary>
    Task DisconnectAsync();
}
