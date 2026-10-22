namespace Synchro.Core.Torrents;

/// <summary>Disk space of a folder on the server, in bytes.</summary>
/// <param name="TotalBytes">Capacity of the disk the folder is on; 0 when the server doesn't say.</param>
public sealed record StorageInfo(string Path, long FreeBytes, long TotalBytes)
{
    public bool HasTotal => TotalBytes > 0 && TotalBytes >= FreeBytes;
    public long UsedBytes => HasTotal ? TotalBytes - FreeBytes : 0;
}
