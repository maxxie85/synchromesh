using Avalonia.Platform.Storage;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Services;

namespace Synchro.Desktop.Services;

public sealed class FilePicker : IFilePicker
{
    public async Task<AddTorrentSource.TorrentFile?> PickTorrentFileAsync(CancellationToken ct = default)
    {
        var window = WindowLocator.Active();

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a torrent file",
            AllowMultiple = false,
            // "All files" stays available: macOS only knows the .torrent type once some app has registered it.
            FileTypeFilter =
            [
                new FilePickerFileType("Torrent files")
                {
                    Patterns = ["*.torrent"],
                    MimeTypes = ["application/x-bittorrent"],
                },
                FilePickerFileTypes.All,
            ],
        });

        var file = files.FirstOrDefault();
        return file is null ? null : await TorrentFileReader.ReadAsync(file, ct);
    }

    public async Task<string?> PickCertificateFileAsync(string title, CancellationToken ct = default)
    {
        var files = await WindowLocator.Active().StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Certificates and keys") { Patterns = ["*.pem", "*.crt", "*.cer", "*.key", "*.p12", "*.pfx"] },
                FilePickerFileTypes.All,
            ],
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }
}
