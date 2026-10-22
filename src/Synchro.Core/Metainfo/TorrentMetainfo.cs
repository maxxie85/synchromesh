using System.Text;

namespace Synchro.Core.Metainfo;

/// <summary>One file listed in a .torrent.</summary>
/// <param name="Index">Position in the torrent's file list; the same index the server uses for this file.</param>
/// <param name="Path">Torrent name and path, separated by '/', as the server reports it.</param>
/// <param name="IsPadding">BEP 47 padding file: real in the file list, never interesting to the user.</param>
public sealed record MetainfoFile(int Index, string Path, long Length, bool IsPadding);

/// <summary>What the Add dialog needs from a .torrent file: its name and file list.</summary>
public sealed record TorrentMetainfo(string Name, IReadOnlyList<MetainfoFile> Files)
{
    /// <summary>
    /// Reads the file list the way libtransmission 4.1 does, so indices match the server's: the v1 `info.files`
    /// array in order (preferring `path.utf-8`, padding files included), or `info.length` for a single file.
    /// A v2-only torrent (`file tree` without `files`) is not supported by the server and yields false.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> torrentFile, out TorrentMetainfo? metainfo)
    {
        metainfo = null;
        try
        {
            if (Bencode.Decode(torrentFile) is not Dictionary<string, object> root
                || !root.TryGetValue("info", out var infoValue) || infoValue is not Dictionary<string, object> info)
                return false;

            var name = Text(info, "name.utf-8") ?? Text(info, "name");
            if (string.IsNullOrEmpty(name)) return false;

            if (info.TryGetValue("files", out var filesValue) && filesValue is List<object> entries)
            {
                var files = new List<MetainfoFile>(entries.Count);
                foreach (var entry in entries)
                {
                    if (entry is not Dictionary<string, object> file || !file.TryGetValue("length", out var length) || length is not long bytes)
                        return false;

                    var segments = Segments(file, "path.utf-8") ?? Segments(file, "path");
                    if (segments is null || segments.Count == 0) return false;

                    var isPadding = (Text(file, "attr")?.Contains('p') ?? false) || segments[0] == ".pad";
                    files.Add(new MetainfoFile(files.Count, $"{name}/{string.Join('/', segments)}", bytes, isPadding));
                }

                if (files.Count == 0) return false;
                metainfo = new TorrentMetainfo(name, files);
                return true;
            }

            if (info.TryGetValue("length", out var single) && single is long singleLength)
            {
                metainfo = new TorrentMetainfo(name, [new MetainfoFile(0, name, singleLength, false)]);
                return true;
            }

            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? Text(Dictionary<string, object> dict, string key) =>
        dict.TryGetValue(key, out var value) && value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : null;

    private static List<string>? Segments(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value) || value is not List<object> parts) return null;

        var segments = new List<string>(parts.Count);
        foreach (var part in parts)
        {
            if (part is not byte[] bytes) return null;
            var text = Encoding.UTF8.GetString(bytes);
            if (text.Length > 0) segments.Add(text); // libtransmission drops empty path segments
        }
        return segments;
    }
}
