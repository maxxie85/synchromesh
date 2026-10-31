using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Synchro.AppServices.Connection;
using Synchro.Core.Servers;
using Synchro.Core.Settings;
using Synchro.ViewModels.Details;
using Synchro.ViewModels.Settings;
using Synchro.ViewModels.Services;

namespace Synchro.ViewModels;

public sealed partial class MainWindowViewModel(
    IServerProfileStore profiles,
    IAppSettingsStore settings,
    IDialogService dialogs,
    IExternalAddQueue externalAdds,
    StatusBarViewModel statusBar,
    TorrentListViewModel torrentList,
    ToolbarViewModel toolbar,
    HelpViewModel help,
    TorrentDetailsViewModel details,
    StatisticsViewModel statistics,
    SpeedHistoryViewModel speedHistory,
    StorageViewModel storage,
    AltSpeedViewModel altSpeed,
    ServersViewModel servers) : ObservableObject
{
    public StatusBarViewModel StatusBar { get; } = statusBar;
    public TorrentListViewModel TorrentList { get; } = torrentList;
    public ToolbarViewModel Toolbar { get; } = toolbar;
    public HelpViewModel Help { get; } = help;
    public TorrentDetailsViewModel Details { get; } = details;
    public StatisticsViewModel Statistics { get; } = statistics;
    public SpeedHistoryViewModel SpeedHistory { get; } = speedHistory;
    public StorageViewModel Storage { get; } = storage;
    public AltSpeedViewModel AltSpeed { get; } = altSpeed;
    public ServersViewModel Servers { get; } = servers;

    /// <summary>
    /// First run opens the server setup; otherwise connects to the server used last (or the first one). Torrents
    /// opened from outside the app (Finder, a magnet link) are held until this is done.
    /// </summary>
    [RelayCommand]
    private async Task InitializeAsync(CancellationToken ct)
    {
        try
        {
            var saved = await profiles.LoadAsync(ct);
            var appSettings = await settings.LoadAsync(ct);
            var lastServerId = appSettings.LastServerId;

            // The sort chosen last time, before any rows arrive; then remember every change.
            TorrentList.Sort = appSettings.ListSort ?? TorrentSort.Default;
            ColumnWidths = appSettings.ListColumnWidths;
            TorrentList.PropertyChanged += OnTorrentListPropertyChanged;

            var profile = saved.FirstOrDefault(p => p.Id == lastServerId)
                          ?? saved.FirstOrDefault()
                          ?? await dialogs.ShowServerSetupAsync(null, ct);

            if (profile is not null)
                await Servers.ConnectToAsync(profile, ct);

            await Servers.LoadAsync(ct);
        }
        finally
        {
            externalAdds.Start();
        }
    }

    /// <summary>The torrent list's saved column widths, for the view to apply once the settings are read.</summary>
    [ObservableProperty] private IReadOnlyDictionary<TorrentSortColumn, double>? _columnWidths;

    /// <summary>
    /// Remembers the list's column widths (when the window closes). Waits for the write, so the app can quit right
    /// after; a failure only means the default widths next time.
    /// </summary>
    public async Task SaveColumnWidthsAsync(IReadOnlyDictionary<TorrentSortColumn, double> widths)
    {
        try
        {
            await settings.UpdateAsync(s => s with { ListColumnWidths = widths.Count == 0 ? null : widths })
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async void OnTorrentListPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TorrentListViewModel.Sort)) return;

        var sort = TorrentList.Sort;
        try
        {
            await settings.UpdateAsync(s => s with { ListSort = sort == TorrentSort.Default ? null : sort });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The list is sorted anyway; it just won't be remembered for the next start.
        }
    }

    /// <summary>Settings… (Cmd+, / Ctrl+,) and Settings… in the server menu: servers, server preferences, appearance.</summary>
    [RelayCommand]
    private Task OpenSettingsAsync(CancellationToken ct) => dialogs.ShowSettingsAsync(SettingsSection.Servers, ct);
}
