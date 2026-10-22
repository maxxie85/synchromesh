using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Synchro.AppServices.Connection;
using Synchro.Core.Servers;
using Synchro.Core.Settings;
using Synchro.ViewModels.Services;

namespace Synchro.ViewModels.Settings;

/// <summary>
/// The saved servers: shown in Settings (add, edit, remove, connect) and in the toolbar's server menu (switch).
/// Connecting remembers the server, so the app reconnects to it on the next start.
/// </summary>
public sealed partial class ServersViewModel : ObservableObject
{
    private readonly IServerProfileStore _profiles;
    private readonly ICredentialStore _credentials;
    private readonly IConnectionService _connection;
    private readonly IAppSettingsStore _settings;
    private readonly IDialogService _dialogs;

    public ServersViewModel(
        IServerProfileStore profiles,
        ICredentialStore credentials,
        IConnectionService connection,
        IAppSettingsStore settings,
        IDialogService dialogs)
    {
        _profiles = profiles;
        _credentials = credentials;
        _connection = connection;
        _settings = settings;
        _dialogs = dialogs;

        _connection.StatusChanged += (_, _) => UpdateActive();
    }

    public ObservableCollection<ServerItemViewModel> Servers { get; } = [];

    /// <summary>The row selected in the Settings list; Edit and Remove act on it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(RemoveCommand), nameof(ConnectCommand))]
    private ServerItemViewModel? _selected;

    /// <summary>For the toolbar: the connected server's name.</summary>
    public string ActiveServerName => _connection.ActiveProfile?.Name ?? "No server";

    public bool HasServers => Servers.Count > 0;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var saved = await _profiles.LoadAsync(ct);
        var selectedId = Selected?.Id;

        Servers.Clear();
        foreach (var profile in saved.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            Servers.Add(new ServerItemViewModel(profile));

        Selected = Servers.FirstOrDefault(s => s.Id == selectedId)
                   ?? Servers.FirstOrDefault(s => s.Id == _connection.ActiveProfile?.Id)
                   ?? Servers.FirstOrDefault();
        UpdateActive();
        OnPropertyChanged(nameof(HasServers));
    }

    /// <summary>Connects to <paramref name="profile"/> and remembers it for the next start.</summary>
    public async Task ConnectToAsync(ServerProfile profile, CancellationToken ct = default)
    {
        await _connection.ConnectAsync(profile, ct);
        try
        {
            await _settings.UpdateAsync(s => s with { LastServerId = profile.Id }, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not remembering the server only means the next start may pick another one; no need to bother the user.
        }
    }

    [RelayCommand]
    private async Task AddAsync(CancellationToken ct)
    {
        var added = await _dialogs.ShowServerSetupAsync(null, ct);
        if (added is null) return;

        await LoadAsync(ct);
        Selected = Servers.FirstOrDefault(s => s.Id == added.Id);

        // The first server (or the only reachable one) is what the user wants to see.
        if (_connection.ActiveProfile is null || _connection.Status.State != ConnectionState.Connected)
            await ConnectToAsync(added, ct);
    }

    private bool HasSelection(ServerItemViewModel? item) => (item ?? Selected) is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task EditAsync(ServerItemViewModel? item, CancellationToken ct)
    {
        if ((item ?? Selected) is not { } server) return;

        var updated = await _dialogs.ShowServerSetupAsync(server.Profile, ct);
        if (updated is null) return;

        await LoadAsync(ct);

        // New address or credentials for the connected server: reconnect with them.
        if (_connection.ActiveProfile?.Id == updated.Id)
            await ConnectToAsync(updated, ct);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RemoveAsync(ServerItemViewModel? item, CancellationToken ct)
    {
        if ((item ?? Selected) is not { } server) return;

        var confirmed = await _dialogs.ConfirmAsync(
            $"Remove “{server.Name}”?",
            "It is removed from this app, with its saved password. The server and its torrents are not touched.",
            "Remove", ct);
        if (!confirmed) return;

        try
        {
            await _profiles.DeleteAsync(server.Id, ct);
            await _credentials.DeleteAsync(server.Id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _dialogs.ShowMessageAsync("Couldn't remove the server", ex.Message, ct);
            await LoadAsync(ct);
            return;
        }

        var wasActive = _connection.ActiveProfile?.Id == server.Id;
        if (Selected?.Id == server.Id) Selected = null;
        await LoadAsync(ct);

        // Removing the connected server moves on to another one, or leaves the app disconnected.
        if (wasActive)
        {
            if (Servers.FirstOrDefault() is { } next)
                await ConnectToAsync(next.Profile, ct);
            else
                await _connection.DisconnectAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ConnectAsync(ServerItemViewModel? item, CancellationToken ct)
    {
        if ((item ?? Selected) is not { } server) return;

        // Already connected to it: nothing to do (a failed or lost connection is retried, though).
        if (_connection.ActiveProfile?.Id == server.Id && _connection.Status.State == ConnectionState.Connected) return;

        await ConnectToAsync(server.Profile, ct);
    }

    // Marks the server in use with its real connection state, so the list never says "Connected" when it isn't.
    private void UpdateActive()
    {
        var activeId = _connection.ActiveProfile?.Id;
        var status = _connection.Status;
        foreach (var server in Servers)
            server.SetState(server.Id == activeId, status.State, status.Message);
        OnPropertyChanged(nameof(ActiveServerName));
    }
}
