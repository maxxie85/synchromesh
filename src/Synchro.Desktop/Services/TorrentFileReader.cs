using Avalonia.Platform.Storage;
using Synchro.Core.Torrents;

namespace Synchro.Desktop.Services;

/// <summary>Reads a .torrent file the user picked, dropped, or opened from Finder.</summary>
internal static class TorrentFileReader
{
    // A real .torrent is a few hundred KB at most; anything near this size is not a torrent.
    private const long MaxBytes = 10 * 1024 * 1024;

    public static async Task<AddTorrentSource.TorrentFile> ReadAsync(IStorageFile file, CancellationToken ct = default)
    {
        await using var stream = await file.OpenReadAsync();
        return await ReadAsync(file.Name, stream, ct);
    }

    public static async Task<AddTorrentSource.TorrentFile> ReadAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        return await ReadAsync(Path.GetFileName(path), stream, ct);
    }

    private static async Task<AddTorrentSource.TorrentFile> ReadAsync(string name, Stream stream, CancellationToken ct)
    {
        if (stream.CanSeek && stream.Length > MaxBytes)
            throw new InvalidOperationException($"“{name}” is too large to be a torrent file.");

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);

        if (buffer.Length > MaxBytes)
            throw new InvalidOperationException($"“{name}” is too large to be a torrent file.");

        return new AddTorrentSource.TorrentFile(name, buffer.ToArray());
    }
}
