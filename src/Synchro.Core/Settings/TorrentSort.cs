namespace Synchro.Core.Settings;

/// <summary>A column of the torrent list that it can be sorted by.</summary>
public enum TorrentSortColumn
{
    /// <summary>The server's queue order (the # column): the default.</summary>
    Queue,
    Name,
    Size,
    Progress,
    Status,
    Download,
    Upload,
    Eta,
    Ratio,
}

/// <summary>How the torrent list is sorted; kept in the settings so it survives a restart.</summary>
public sealed record TorrentSort(TorrentSortColumn Column, bool Descending)
{
    public static TorrentSort Default { get; } = new(TorrentSortColumn.Queue, false);
}
