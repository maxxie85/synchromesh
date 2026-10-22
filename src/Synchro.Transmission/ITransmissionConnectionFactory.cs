using Synchro.Core.Servers;

namespace Synchro.Transmission;

public interface ITransmissionConnectionFactory
{
    TransmissionConnection Create(ServerProfile profile, string? password);
}
