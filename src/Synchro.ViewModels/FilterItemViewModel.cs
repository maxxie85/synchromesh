using CommunityToolkit.Mvvm.ComponentModel;
using Synchro.Core.Torrents;

namespace Synchro.ViewModels;

public enum TorrentFilter
{
    All,
    Downloading,
    Seeding,
    Paused,
    Errors,
}

public sealed partial class FilterItemViewModel(TorrentFilter filter, string title) : ObservableObject
{
    public TorrentFilter Filter { get; } = filter;
    public string Title { get; } = title;

    [ObservableProperty] private int _count;

    /// <summary>True for the active filter; drives the checkmark in the View menu.</summary>
    [ObservableProperty] private bool _isSelected;

    public static bool Matches(TorrentFilter filter, Torrent t) => filter switch
    {
        TorrentFilter.All => true,
        TorrentFilter.Downloading => t.Status is TorrentStatus.Downloading or TorrentStatus.QueuedToDownload,
        TorrentFilter.Seeding => t.Status is TorrentStatus.Seeding or TorrentStatus.QueuedToSeed,
        TorrentFilter.Paused => t.Status == TorrentStatus.Stopped,
        TorrentFilter.Errors => t.HasError,
        _ => true,
    };
}
