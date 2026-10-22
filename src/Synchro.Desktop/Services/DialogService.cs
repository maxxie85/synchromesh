using Synchro.Core.Servers;
using Synchro.Core.Torrents;
using Synchro.Desktop.Views;
using Synchro.ViewModels;
using Synchro.ViewModels.Services;
using Synchro.ViewModels.Settings;

namespace Synchro.Desktop.Services;

public sealed class DialogService(
    Func<ServerSetupViewModel> serverSetupFactory,
    Func<AddTorrentViewModel> addTorrentFactory,
    Func<SettingsViewModel> settingsFactory,
    IUrlLauncher urlLauncher) : IDialogService
{
    public async Task<ServerProfile?> ShowServerSetupAsync(ServerProfile? existing, CancellationToken ct = default)
    {
        var viewModel = serverSetupFactory();
        await viewModel.InitializeAsync(existing, ct);

        var window = new ServerSetupWindow { DataContext = viewModel };
        viewModel.CloseRequested += (_, profile) => window.Close(profile);

        // Opened from the main window at first start, or from Settings: attach to the one in front.
        return await window.ShowDialog<ServerProfile?>(WindowLocator.Active());
    }

    public async Task<AddTorrentResult?> ShowAddTorrentAsync(AddTorrentSource? initial, CancellationToken ct = default)
    {
        var viewModel = addTorrentFactory();

        var window = new AddTorrentWindow { DataContext = viewModel };
        viewModel.CloseRequested += (_, result) => window.Close(result);
        // Closed with the window's close button while a link waits for file choices: take the paused torrent back.
        // A no-op once the dialog finished or cancelled itself.
        window.Closed += (_, _) => _ = viewModel.AbandonAsync();

        // Not awaited: the dialog opens at once with the link or file filled in, and the server's
        // default folder and free space appear when they arrive.
        _ = viewModel.InitializeAsync(initial, ct);

        return await window.ShowDialog<AddTorrentResult?>(WindowLocator.MainWindow());
    }

    public async Task<RemoveRequest?> ConfirmRemoveAsync(IReadOnlyList<string> torrentNames, CancellationToken ct = default)
    {
        var viewModel = new ConfirmRemoveViewModel(torrentNames);

        var window = new ConfirmRemoveWindow { DataContext = viewModel };
        viewModel.CloseRequested += (_, request) => window.Close(request);

        return await window.ShowDialog<RemoveRequest?>(WindowLocator.MainWindow());
    }

    public async Task<SpeedLimits?> ShowSpeedLimitsAsync(Func<CancellationToken, Task<SpeedLimits>> load, CancellationToken ct = default)
    {
        var viewModel = new SpeedLimitsViewModel(load);

        var window = new SpeedLimitsWindow { DataContext = viewModel };
        viewModel.CloseRequested += (_, limits) => window.Close(limits);

        // Not awaited: the dialog opens now and fills in when the server answers.
        _ = viewModel.LoadAsync(ct);

        return await window.ShowDialog<SpeedLimits?>(WindowLocator.MainWindow());
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>?> ShowLabelsAsync(
        IReadOnlyList<string> knownLabels, IReadOnlyDictionary<string, IReadOnlyList<string>> labelsByHash, CancellationToken ct = default)
    {
        var viewModel = new LabelsViewModel(knownLabels, labelsByHash);

        var window = new LabelsWindow { DataContext = viewModel };
        viewModel.CloseRequested += (_, result) => window.Close(result);

        return await window.ShowDialog<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(WindowLocator.MainWindow());
    }

    public async Task<MoveDataRequest?> ShowMoveDataAsync(IReadOnlyList<string> torrentNames, string currentLocation, CancellationToken ct = default)
    {
        var viewModel = new MoveDataViewModel(torrentNames, currentLocation);

        var window = new MoveDataWindow { DataContext = viewModel };
        viewModel.CloseRequested += (_, request) => window.Close(request);

        return await window.ShowDialog<MoveDataRequest?>(WindowLocator.MainWindow());
    }

    public async Task<string?> PromptAsync(TextPrompt prompt, CancellationToken ct = default)
    {
        var viewModel = new TextPromptViewModel(prompt);

        var window = new TextPromptWindow { DataContext = viewModel };
        viewModel.CloseRequested += (_, text) => window.Close(text);

        return await window.ShowDialog<string?>(WindowLocator.Active());
    }

    public Task ShowMessageAsync(string title, string message, CancellationToken ct = default)
        => new MessageWindow(title, message).ShowDialog(WindowLocator.Active());

    public Task ShowAboutAsync(CancellationToken ct = default)
        => new AboutWindow { DataContext = new AboutViewModel(urlLauncher, AppInfo.Version) }.ShowDialog(WindowLocator.MainWindow());

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, CancellationToken ct = default)
        => new ConfirmWindow(title, message, confirmText).ShowDialog<bool>(WindowLocator.Active());

    public async Task ShowSettingsAsync(SettingsSection section, CancellationToken ct = default)
    {
        var viewModel = settingsFactory();
        await viewModel.LoadAsync(section, ct);
        await new SettingsWindow { DataContext = viewModel }.ShowDialog(WindowLocator.MainWindow());
    }
}
