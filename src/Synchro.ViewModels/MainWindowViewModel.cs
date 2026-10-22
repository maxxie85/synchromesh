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
            var lastServerId = (await settings.LoadAsync(ct)).LastServerId;
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

    /// <summary>Settings… (Cmd+, / Ctrl+,) and Settings… in the server menu: servers, server preferences, appearance.</summary>
    [RelayCommand]
    private Task OpenSettingsAsync(CancellationToken ct) => dialogs.ShowSettingsAsync(SettingsSection.Servers, ct);
}
