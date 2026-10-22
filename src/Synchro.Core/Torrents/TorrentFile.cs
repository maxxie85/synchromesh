namespace Synchro.Core.Torrents;

/// <summary>File download priority; values match tr_priority_t.</summary>
public enum FilePriority
{
    Low = -1,
    Normal = 0,
    High = 1,
}

/// <summary>One file in a torrent.</summary>
/// <param name="Index">Position in the torrent's file list: how the protocol addresses the file.</param>
/// <param name="Path">Path inside the torrent, folders separated by '/'.</param>
public sealed record TorrentFile(int Index, string Path, long Length, long BytesCompleted, bool Wanted, FilePriority Priority);

/// <summary>A file's changing state, without its name and size: what a poll of an open Files tab needs.</summary>
public sealed record TorrentFileProgress(long BytesCompleted, bool Wanted, FilePriority Priority);

/// <summary>How much of a torrent's file list to fetch with its details.</summary>
public enum DetailFiles
{
    None,

    /// <summary>Names, sizes and progress: <see cref="TorrentDetails.Files"/>.</summary>
    NamesAndProgress,

    /// <summary>
    /// Only progress, in file order: <see cref="TorrentDetails.FileProgress"/>. Much smaller for a torrent with
    /// thousands of files; the names come from an earlier <see cref="NamesAndProgress"/> fetch.
    /// </summary>
    ProgressOnly,
}

/// <summary>
/// A torrent's files as the server knows them. For a magnet link the server first has to fetch the file list
/// (the metadata) from peers: until <see cref="MetadataPercentComplete"/> reaches 1, <see cref="Files"/> is empty.
/// </summary>
public sealed record TorrentFileList(string Name, double MetadataPercentComplete, IReadOnlyList<TorrentFile> Files)
{
    public bool HasMetadata => MetadataPercentComplete >= 1 && Files.Count > 0;
}
