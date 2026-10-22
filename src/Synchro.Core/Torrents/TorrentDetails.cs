namespace Synchro.Core.Torrents;

/// <summary>
/// Everything the details panel shows for one torrent. Sizes are bytes, rates bytes per second,
/// speed limits kB/s (as in the protocol). Dates are null when the server reports none (0).
/// <see cref="Files"/> is null when files were not requested (they can be large, so only the Files tab asks).
/// </summary>
/// <param name="Eta">Seconds remaining; negative when unknown.</param>
/// <param name="UploadRatio">Negative when not available.</param>
public sealed record TorrentDetails(
    string HashString,
    string Name,
    TorrentStatus Status,
    bool IsStalled,
    double PercentDone,
    int Error,
    string? ErrorString,
    string DownloadDirectory,
    long TotalSize,
    long SizeWhenDone,
    long LeftUntilDone,
    long HaveValid,
    long DownloadedEver,
    long UploadedEver,
    long CorruptEver,
    double UploadRatio,
    long RateDownload,
    long RateUpload,
    long Eta,
    bool DownloadLimited,
    int DownloadLimitKilobytes,
    bool UploadLimited,
    int UploadLimitKilobytes,
    int PeersConnected,
    int PeersSendingToUs,
    int PeersGettingFromUs,
    int PeerLimit,
    DateTimeOffset? AddedDate,
    DateTimeOffset? DoneDate,
    DateTimeOffset? ActivityDate,
    DateTimeOffset? DateCreated,
    string? Creator,
    string? Comment,
    int PieceCount,
    long PieceSize,
    bool IsPrivate,
    string? MagnetLink,
    IReadOnlyList<TrackerInfo> Trackers,
    IReadOnlyList<PeerInfo> Peers,
    IReadOnlyList<TorrentFile>? Files = null,
    TorrentOptions? Options = null,
    IReadOnlyList<string>? Labels = null,
    IReadOnlyList<TorrentFileProgress>? FileProgress = null)
{
    public bool HasError => Error != 0;
}

/// <summary>Tracker state, as in `announce_state` (transmission.h tr_tracker_state).</summary>
public enum TrackerAnnounceState
{
    Inactive = 0,
    Waiting = 1,
    Queued = 2,
    Active = 3,
}

/// <summary>One tracker of a torrent with its latest announce. Counts are negative when the tracker didn't say.</summary>
public sealed record TrackerInfo(
    int Id,
    int Tier,
    string Announce,
    string Host,
    bool IsBackup,
    TrackerAnnounceState AnnounceState,
    bool HasAnnounced,
    bool LastAnnounceSucceeded,
    bool LastAnnounceTimedOut,
    string? LastAnnounceResult,
    DateTimeOffset? NextAnnounceTime,
    int SeederCount,
    int LeecherCount,
    int DownloadCount,
    int LastAnnouncePeerCount);

/// <summary>A connected peer. Rates are from our point of view: <see cref="RateToClient"/> is downloading from them.</summary>
public sealed record PeerInfo(
    string Address,
    int Port,
    string ClientName,
    string FlagString,
    double Progress,
    long RateToClient,
    long RateToPeer,
    bool IsEncrypted,
    bool IsIncoming,
    bool IsUtp);
