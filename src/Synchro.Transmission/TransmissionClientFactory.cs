using Synchro.Core.Servers;
using Synchro.Core.Torrents;

namespace Synchro.Transmission;

public sealed class TransmissionClientFactory(ITransmissionConnectionFactory connections) : ITransmissionClientFactory
{
    // Every supported server speaks RPC 6.x JSON-RPC; another protocol would get its own client here.
    public ITransmissionClient Create(ServerProfile profile, string? password, ServerCapabilities capabilities)
        => new TransmissionClient(connections.Create(profile, password), capabilities);
}
