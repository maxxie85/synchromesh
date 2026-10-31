using Synchro.Core.Settings;
using Avalonia.Data.Converters;
using Material.Icons;
using Synchro.Core.Torrents;
using Synchro.ViewModels;

namespace Synchro.Desktop.Converters;

/// <summary>Picks Material icons for view model values. Icons are a view concern, so this lives here, not in the view models.</summary>
public static class IconConverters
{
    public static readonly FuncValueConverter<TorrentFilter, MaterialIconKind> Filter = new(filter => filter switch
    {
        TorrentFilter.All => MaterialIconKind.ViewList,
        TorrentFilter.Downloading => MaterialIconKind.Download,
        TorrentFilter.Seeding => MaterialIconKind.Upload,
        TorrentFilter.Paused => MaterialIconKind.Pause,
        TorrentFilter.Errors => MaterialIconKind.AlertCircleOutline,
        _ => MaterialIconKind.ViewList,
    });

    public static readonly FuncValueConverter<TorrentStatus, MaterialIconKind> Status = new(status => status switch
    {
        TorrentStatus.Downloading => MaterialIconKind.Download,
        TorrentStatus.Seeding => MaterialIconKind.Upload,
        TorrentStatus.Stopped => MaterialIconKind.Pause,
        TorrentStatus.Verifying or TorrentStatus.QueuedToVerify => MaterialIconKind.Sync,
        _ => MaterialIconKind.ClockOutline, // queued to download or seed
    });
}

/// <summary>Groups torrent states into the few colours the list uses.</summary>
public static class StatusConverters
{
    public static readonly FuncValueConverter<TorrentStatus, bool> IsDownloading =
        new(s => s is TorrentStatus.Downloading or TorrentStatus.Verifying);

    public static readonly FuncValueConverter<TorrentStatus, bool> IsSeeding = new(s => s == TorrentStatus.Seeding);
}

/// <summary>The torrent list's column headers: which one is sorted, and the arrow for its direction.</summary>
public static class SortConverters
{
    /// <summary>True for the header of the sorted column (the column is the converter parameter).</summary>
    public static readonly FuncValueConverter<TorrentSort, TorrentSortColumn, bool> IsColumn =
        new((sort, column) => sort is not null && sort.Column == column);

    public static readonly FuncValueConverter<TorrentSort, MaterialIconKind> Arrow =
        new(sort => sort is { Descending: true } ? MaterialIconKind.MenuDown : MaterialIconKind.MenuUp);
}
