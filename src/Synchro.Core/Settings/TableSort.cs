namespace Synchro.Core.Settings;

/// <summary>
/// How one of the details tables (trackers, peers, files) is sorted: a column, by its key, and the direction. Kept in
/// the settings so it survives a restart. The torrent list has its own typed <see cref="TorrentSort"/>.
/// </summary>
public sealed record TableSort(string Column, bool Descending);
