namespace Synchro.Core.Torrents;

/// <summary>Server-wide speed limits. Rates are in kB/s, as the RPC protocol uses.</summary>
public sealed record SpeedLimits(
    bool DownloadLimited,
    int DownloadKilobytesPerSecond,
    bool UploadLimited,
    int UploadKilobytesPerSecond,
    bool AlternativeSpeedsEnabled);

/// <summary>The server's alternative ("turtle") speed limits, in kB/s, and whether they are on.</summary>
public sealed record AltSpeed(bool Enabled, int DownloadKilobytesPerSecond, int UploadKilobytesPerSecond);
