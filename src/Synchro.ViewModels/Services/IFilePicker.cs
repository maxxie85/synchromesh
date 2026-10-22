using Synchro.Core.Torrents;

namespace Synchro.ViewModels.Services;

public interface IFilePicker
{
    /// <summary>Lets the user choose a .torrent file and reads it. Returns null when cancelled.</summary>
    Task<AddTorrentSource.TorrentFile?> PickTorrentFileAsync(CancellationToken ct = default);

    /// <summary>Lets the user choose a client certificate or key file; returns its local path, or null when cancelled.</summary>
    Task<string?> PickCertificateFileAsync(string title, CancellationToken ct = default);
}
