using Synchro.AppServices.Connection;
using Synchro.Core.Torrents;

namespace Synchro.ViewModels.Services;

public sealed class AddTorrentFlow(
    IDialogService dialogs,
    IClipboardService clipboard,
    IConnectionService connection) : IAddTorrentFlow
{
    public async Task AddManuallyAsync(CancellationToken ct = default)
    {
        if (!await EnsureConnectedAsync()) return;

        await ShowOneAsync(null, ct);
    }

    public async Task AddAsync(IReadOnlyList<AddTorrentSource> sources, CancellationToken ct = default)
    {
        if (!await EnsureConnectedAsync()) return;

        if (sources.Count == 0)
        {
            await dialogs.ShowMessageAsync("Nothing to add", "Drop .torrent files or magnet links to add them.", ct);
            return;
        }

        foreach (var source in sources)
            await ShowOneAsync(source, ct);
    }

    public async Task AddFromClipboardAsync(CancellationToken ct = default)
    {
        if (!await EnsureConnectedAsync()) return;

        var text = await clipboard.GetTextAsync(ct);
        if (AddTorrentSource.TryParseLink(text, out var source))
        {
            await ShowOneAsync(source, ct);
            return;
        }

        await dialogs.ShowMessageAsync(
            "No torrent link on the clipboard",
            "Copy a magnet link (or a link to a .torrent file) first, then paste again.", ct);
    }

    private async Task ShowOneAsync(AddTorrentSource? source, CancellationToken ct)
    {
        var result = await dialogs.ShowAddTorrentAsync(source, ct);

        if (result is { Outcome: AddTorrentOutcome.Duplicate })
        {
            var name = string.IsNullOrWhiteSpace(result.Name) ? "This torrent" : $"“{result.Name}”";
            await dialogs.ShowMessageAsync("Already on the server", $"{name} is already on the server, so nothing was added.", ct);
        }
    }

    private async Task<bool> EnsureConnectedAsync()
    {
        if (connection.Status.State == ConnectionState.Connected) return true;

        await dialogs.ShowMessageAsync("Not connected", "Connect to a server first, then add the torrent.");
        return false;
    }
}
