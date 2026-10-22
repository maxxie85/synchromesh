namespace Synchro.Core.Torrents;

/// <summary>A snapshot of one torrent. Speeds are bytes per second, sizes are bytes.</summary>
/// <param name="Id">Numeric id: only stable while the daemon runs. Use <see cref="HashString"/> as identity.</param>
/// <param name="Eta">Seconds remaining; negative when unknown.</param>
/// <param name="UploadRatio">Negative when not available.</param>
/// <param name="QueuePosition">Zero-based position in the server's queue.</param>
public sealed record Torrent(
    int Id,
    string HashString,
    string Name,
    TorrentStatus Status,
    double PercentDone,
    long TotalSize,
    long RateDownload,
    long RateUpload,
    long Eta,
    double UploadRatio,
    int Error,
    string? ErrorString,
    bool IsStalled,
    DateTimeOffset AddedDate,
    int QueuePosition,
    IReadOnlyList<string>? Labels = null)
{
    public bool HasError => Error != 0;

    /// <summary>The torrent's labels (tags the user gave it); never null.</summary>
    public IReadOnlyList<string> LabelList => Labels ?? [];
}
