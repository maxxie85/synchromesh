namespace Synchro.Core.Servers;

/// <summary>How Transmission encrypts peer connections; the protocol's `encryption` strings.</summary>
public enum EncryptionMode
{
    /// <summary>Only connect to peers that encrypt.</summary>
    Required,

    /// <summary>Encrypt when the peer supports it (Transmission's default).</summary>
    Preferred,

    /// <summary>Accept unencrypted connections too.</summary>
    Allowed,
}

/// <summary>
/// The daemon's own preferences that the app lets the user change (session_get / session_set). Speeds in kB/s, times
/// in minutes after midnight, idle limits in minutes; <see cref="AltSpeedTimeDays"/> is tr_sched_day (Sunday = 1,
/// Monday = 2, …, Saturday = 64). <see cref="BlocklistUrl"/> is where <c>blocklist_update</c> downloads the list from.
/// </summary>
public sealed record ServerPreferences(
    string DownloadDirectory,
    bool IncompleteDirectoryEnabled,
    string IncompleteDirectory,
    bool StartAddedTorrents,
    bool RenamePartialFiles,
    bool TrashOriginalTorrentFiles,
    bool DownloadQueueEnabled,
    int DownloadQueueSize,
    bool SeedQueueEnabled,
    int SeedQueueSize,
    bool SeedRatioLimited,
    double SeedRatioLimit,
    bool IdleSeedingLimitEnabled,
    int IdleSeedingLimitMinutes,
    int PeerLimitGlobal,
    int PeerLimitPerTorrent,
    EncryptionMode Encryption,
    bool DhtEnabled,
    bool PexEnabled,
    bool LpdEnabled,
    int PeerPort,
    bool PortForwardingEnabled,
    int AltSpeedDownKilobytes,
    int AltSpeedUpKilobytes,
    bool AltSpeedTimeEnabled,
    int AltSpeedTimeBegin,
    int AltSpeedTimeEnd,
    int AltSpeedTimeDays,
    bool BlocklistEnabled = false,
    string BlocklistUrl = "");

/// <summary>Whether the peer port is reachable from the internet, for one IP version.</summary>
/// <param name="IsOpen">Null when the test couldn't run for this IP version (for example no IPv6).</param>
public sealed record PortTestResult(string IpProtocol, bool? IsOpen);
