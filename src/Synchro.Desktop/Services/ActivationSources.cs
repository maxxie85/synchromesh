using System.Diagnostics;
using Avalonia.Platform.Storage;
using Synchro.Core.Torrents;

namespace Synchro.Desktop.Services;

/// <summary>
/// Turns what the OS hands the app (opened files, a magnet: URL, command-line arguments) into add sources.
/// Anything that isn't a readable .torrent file or a torrent link is skipped, so one bad item doesn't block the rest.
/// </summary>
internal static class ActivationSources
{
    public static async Task<IReadOnlyList<AddTorrentSource>> FromStorageItemsAsync(IEnumerable<IStorageItem> items)
    {
        var sources = new List<AddTorrentSource>();
        foreach (var file in items.OfType<IStorageFile>().Where(f => IsTorrentFileName(f.Name)))
        {
            if (await TryReadAsync(() => TorrentFileReader.ReadAsync(file), file.Name) is { } source)
                sources.Add(source);
        }

        return sources;
    }

    public static async Task<IReadOnlyList<AddTorrentSource>> FromUriAsync(Uri uri)
    {
        if (uri.IsFile)
            return await FromArgumentsAsync([uri.LocalPath]);

        return AddTorrentSource.TryParseLink(uri.OriginalString, out var link) ? [link] : [];
    }

    /// <summary>
    /// Each argument may be a magnet link, a torrent URL, or a .torrent file as a path or a file:// URI (Linux file
    /// managers pass URIs to a .desktop file's %U).
    /// </summary>
    public static async Task<IReadOnlyList<AddTorrentSource>> FromArgumentsAsync(IEnumerable<string> args)
    {
        var sources = new List<AddTorrentSource>();
        foreach (var argument in args)
        {
            var arg = argument.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                      && Uri.TryCreate(argument, UriKind.Absolute, out var fileUri) && fileUri.IsFile
                ? fileUri.LocalPath
                : argument;
            if (AddTorrentSource.TryParseLink(arg, out var link))
                sources.Add(link);
            else if (IsTorrentFileName(arg) && File.Exists(arg)
                     && await TryReadAsync(() => TorrentFileReader.ReadAsync(arg), arg) is { } file)
                sources.Add(file);
        }

        return sources;
    }

    private static bool IsTorrentFileName(string name) => name.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase);

    private static async Task<AddTorrentSource?> TryReadAsync(Func<Task<AddTorrentSource.TorrentFile>> read, string name)
    {
        try
        {
            return await read();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Skipped “{name}”: {ex.Message}");
            return null;
        }
    }
}
