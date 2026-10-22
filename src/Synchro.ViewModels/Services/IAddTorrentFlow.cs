using Synchro.Core.Torrents;

namespace Synchro.ViewModels.Services;

/// <summary>
/// The single way a torrent gets added, whatever the entry point (Add button, drag and drop, paste,
/// and later the macOS file and magnet: association). Each source gets its own confirmation dialog.
/// </summary>
public interface IAddTorrentFlow
{
    /// <summary>Opens an empty Add dialog where the user enters a link or picks a file.</summary>
    Task AddManuallyAsync(CancellationToken ct = default);

    /// <summary>Opens a dialog per source, one after the other.</summary>
    Task AddAsync(IReadOnlyList<AddTorrentSource> sources, CancellationToken ct = default);

    /// <summary>Adds the magnet link or URL on the clipboard.</summary>
    Task AddFromClipboardAsync(CancellationToken ct = default);
}
