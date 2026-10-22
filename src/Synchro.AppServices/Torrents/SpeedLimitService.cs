using Synchro.AppServices.Connection;
using Synchro.Core.Torrents;

namespace Synchro.AppServices.Torrents;

public sealed class SpeedLimitService(IConnectionService connection) : ISpeedLimitService
{
    public Task<SpeedLimits> GetAsync(CancellationToken ct = default) => Client().GetSpeedLimitsAsync(ct);

    public Task SetAsync(SpeedLimits limits, CancellationToken ct = default) => Client().SetSpeedLimitsAsync(limits, ct);

    public async Task<AltSpeed?> GetAltSpeedAsync(CancellationToken ct = default)
        => connection.Client is { } client ? await client.GetAltSpeedAsync(ct) : null;

    public Task SetAltSpeedEnabledAsync(bool enabled, CancellationToken ct = default)
        => Client().SetAltSpeedEnabledAsync(enabled, ct);

    private ITransmissionClient Client()
        => connection.Client ?? throw new InvalidOperationException("Not connected to a server.");
}
