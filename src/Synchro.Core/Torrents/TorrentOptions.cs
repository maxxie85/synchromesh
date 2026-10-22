namespace Synchro.Core.Torrents;

/// <summary>Bandwidth priority; values match tr_priority_t.</summary>
public enum BandwidthPriority
{
    Low = -1,
    Normal = 0,
    High = 1,
}

/// <summary>When a torrent stops seeding by ratio or idle time; values match tr_ratiolimit / tr_idlelimit.</summary>
public enum SeedLimitMode
{
    /// <summary>Use the server's setting.</summary>
    ServerDefault = 0,

    /// <summary>Use this torrent's own limit.</summary>
    Custom = 1,

    /// <summary>Never stop for this reason.</summary>
    Unlimited = 2,
}

/// <summary>Per-torrent settings (torrent_set). Speeds in kB/s as in the protocol; idle limit in minutes.</summary>
/// <param name="HonorsSessionLimits">Also obey the server-wide speed limits.</param>
/// <param name="SequentialDownload">Fetch pieces in order (for watching while it downloads), not rarest first.</param>
public sealed record TorrentOptions(
    bool DownloadLimited,
    int DownloadLimitKilobytes,
    bool UploadLimited,
    int UploadLimitKilobytes,
    bool HonorsSessionLimits,
    BandwidthPriority Priority,
    SeedLimitMode RatioMode,
    double RatioLimit,
    SeedLimitMode IdleMode,
    int IdleLimitMinutes,
    int PeerLimit,
    bool SequentialDownload = false);
