using Synchro.Core.Servers;

namespace Synchro.Core.Torrents;

public interface ITransmissionClientFactory
{
    /// <param name="capabilities">
    /// What the connection test found. The one place to pick a different protocol implementation per server version.
    /// </param>
    ITransmissionClient Create(ServerProfile profile, string? password, ServerCapabilities capabilities);
}
