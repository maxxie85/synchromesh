using Synchro.Core.Torrents;

namespace Synchro.AppServices.Torrents;

public interface ISpeedLimitService
{
    Task<SpeedLimits> GetAsync(CancellationToken ct = default);
    Task SetAsync(SpeedLimits limits, CancellationToken ct = default);

    /// <summary>The alternative ("turtle") limits; null when not connected.</summary>
    Task<AltSpeed?> GetAltSpeedAsync(CancellationToken ct = default);

    Task SetAltSpeedEnabledAsync(bool enabled, CancellationToken ct = default);
}
