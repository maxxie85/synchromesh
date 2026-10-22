using System.Diagnostics.CodeAnalysis;

namespace Synchro.Core.Torrents;

/// <summary>Where a new torrent comes from. One shape for every entry point: dialog, drop, paste, OS activation.</summary>
public abstract record AddTorrentSource
{
    private AddTorrentSource() { }

    /// <param name="Link">The full magnet link.</param>
    /// <param name="DisplayName">The link's `dn` (display name) parameter, when it has one.</param>
    public sealed record Magnet(string Link, string? DisplayName) : AddTorrentSource;

    /// <summary>A link the server downloads itself (usually a .torrent file).</summary>
    public sealed record Url(Uri Address) : AddTorrentSource;

    public sealed record TorrentFile(string FileName, byte[] Content) : AddTorrentSource;

    /// <summary>Recognises a magnet link or an http(s) URL. Surrounding whitespace is ignored.</summary>
    public static bool TryParseLink(string? text, [NotNullWhen(true)] out AddTorrentSource? source)
    {
        source = null;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;

        if (trimmed.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            if (!HasInfoHash(trimmed)) return false;

            source = new Magnet(trimmed, ReadDisplayName(trimmed));
            return true;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            source = new Url(uri);
            return true;
        }

        return false;
    }

    // BitTorrent v1 (btih) and v2 (btmh) info-hashes.
    private static bool HasInfoHash(string magnet) =>
        magnet.Contains("xt=urn:btih:", StringComparison.OrdinalIgnoreCase)
        || magnet.Contains("xt=urn:btmh:", StringComparison.OrdinalIgnoreCase);

    private static string? ReadDisplayName(string magnet)
    {
        var query = magnet[(magnet.IndexOf('?') + 1)..];
        foreach (var part in query.Split('&'))
        {
            if (!part.StartsWith("dn=", StringComparison.OrdinalIgnoreCase)) continue;

            try { return Uri.UnescapeDataString(part[3..].Replace('+', ' ')); }
            catch (UriFormatException) { return null; }
        }

        return null;
    }
}
