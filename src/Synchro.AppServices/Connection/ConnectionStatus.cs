using Synchro.Core.Servers;

namespace Synchro.AppServices.Connection;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Failed,
}

/// <param name="Capabilities">Set while connected (and kept through a dropped connection): the server's RPC version.</param>
public sealed record ConnectionStatus(
    ConnectionState State, string? ServerVersion = null, string? Message = null, ServerCapabilities? Capabilities = null);
