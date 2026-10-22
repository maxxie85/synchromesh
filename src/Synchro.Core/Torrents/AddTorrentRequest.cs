namespace Synchro.Core.Torrents;

/// <param name="DownloadDirectory">A path on the server, or null to use the server's default.</param>
/// <param name="Paused">true adds the torrent without starting it.</param>
/// <param name="Files">File choices made before adding; null downloads everything at normal priority.</param>
/// <param name="SequentialDownload">Download pieces in order instead of rarest first.</param>
public sealed record AddTorrentRequest(
    AddTorrentSource Source, string? DownloadDirectory, bool Paused, FileChoices? Files = null, bool SequentialDownload = false);

/// <summary>
/// Files to skip and non-normal priorities, by file index. Files not listed are downloaded at normal priority,
/// which is the server's default, so only differences are sent.
/// </summary>
public sealed record FileChoices(IReadOnlyList<int> Unwanted, IReadOnlyList<int> HighPriority, IReadOnlyList<int> LowPriority)
{
    public bool IsDefault => Unwanted.Count == 0 && HighPriority.Count == 0 && LowPriority.Count == 0;
}

public enum AddTorrentOutcome
{
    Added,
    Duplicate,
}

public sealed record AddTorrentResult(AddTorrentOutcome Outcome, string HashString, string Name);
