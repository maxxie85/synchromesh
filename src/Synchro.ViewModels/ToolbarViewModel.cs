using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Services;

namespace Synchro.ViewModels;

/// <summary>Commands for the toolbar and the list's context menu. They act on the selected torrents.</summary>
public sealed partial class ToolbarViewModel : ObservableObject, IDisposable
{
    private readonly TorrentListViewModel _list;
    private readonly IConnectionService _connection;
    private readonly ITorrentActions _actions;
    private readonly ISpeedLimitService _speedLimits;
    private readonly IDialogService _dialogs;
    private readonly IAddTorrentFlow _addFlow;

    public ToolbarViewModel(
        TorrentListViewModel list,
        IConnectionService connection,
        ITorrentActions actions,
        ISpeedLimitService speedLimits,
        IDialogService dialogs,
        IAddTorrentFlow addFlow)
    {
        _list = list;
        _connection = connection;
        _actions = actions;
        _speedLimits = speedLimits;
        _dialogs = dialogs;
        _addFlow = addFlow;

        _list.SelectionChanged += OnStateChanged;
        _connection.StatusChanged += OnStateChanged;
    }

    public void Dispose()
    {
        _list.SelectionChanged -= OnStateChanged;
        _connection.StatusChanged -= OnStateChanged;
    }

    private bool IsConnected => _connection.Status.State == ConnectionState.Connected;
    private bool CanActOnSelection() => IsConnected && _list.SelectedTorrents.Count > 0;
    private bool CanChangeSettings() => IsConnected;
    private bool CanActOnOne() => IsConnected && _list.SelectedTorrents.Count == 1;

    private void OnStateChanged(object? sender, EventArgs e)
    {
        AddCommand.NotifyCanExecuteChanged();
        PasteCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        StartNowCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        VerifyCommand.NotifyCanExecuteChanged();
        MoveDataCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        EditLabelsCommand.NotifyCanExecuteChanged();
        ReannounceCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        MoveToTopCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        MoveToBottomCommand.NotifyCanExecuteChanged();
        SpeedLimitsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanChangeSettings))]
    private Task AddAsync() => RunAddAsync(() => _addFlow.AddManuallyAsync());

    /// <summary>Adds the magnet link or URL on the clipboard (the ⌘V shortcut).</summary>
    [RelayCommand(CanExecute = nameof(CanChangeSettings))]
    private Task PasteAsync() => RunAddAsync(() => _addFlow.AddFromClipboardAsync());

    /// <summary>Entry point for sources that arrive from outside the toolbar, such as dropped files.</summary>
    public Task AddSourcesAsync(IReadOnlyList<AddTorrentSource> sources) => RunAddAsync(() => _addFlow.AddAsync(sources));

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task StartAsync()
        => RunAsync("Couldn't start the torrents", SelectedHashes(), hashes => _actions.StartAsync(hashes, startNow: false));

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task StartNowAsync()
        => RunAsync("Couldn't start the torrents", SelectedHashes(), hashes => _actions.StartAsync(hashes, startNow: true));

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task StopAsync()
        => RunAsync("Couldn't stop the torrents", SelectedHashes(), hashes => _actions.StopAsync(hashes));

    /// <summary>Re-checks the selected torrents' data on the server.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task VerifyAsync()
        => RunAsync("Couldn't verify the torrents", SelectedHashes(), hashes => _actions.VerifyAsync(hashes));

    /// <summary>Asks the trackers for more peers now.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task ReannounceAsync()
        => RunAsync("Couldn't contact the trackers", SelectedHashes(), hashes => _actions.ReannounceAsync(hashes));

    /// <summary>Adds or removes labels on the selected torrents.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private async Task EditLabelsAsync()
    {
        var selected = _list.SelectedTorrents;
        if (selected.Count == 0) return;

        var current = selected.ToDictionary(t => t.Hash, t => t.Torrent.LabelList);
        var changed = await _dialogs.ShowLabelsAsync(_list.KnownLabels, current);
        if (changed is not { Count: > 0 }) return;

        await RunAsync("Couldn't change the labels", changed.Keys.ToList(), _ => _actions.SetLabelsAsync(changed));
    }

    /// <summary>Moves the selected torrents' data to another folder on the server, or points them at it.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private async Task MoveDataAsync()
    {
        var selected = _list.SelectedTorrents;
        if (selected.Count == 0) return;

        string current;
        try
        {
            current = await _actions.GetLocationAsync(selected[0].Hash) ?? "";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            current = ""; // the dialog still works; the user types the folder
        }

        var request = await _dialogs.ShowMoveDataAsync(selected.Select(t => t.Name).ToList(), current);
        if (request is null) return;

        await RunAsync("Couldn't move the data", selected.Select(t => t.Hash).ToList(),
            hashes => _actions.MoveDataAsync(hashes, request.Location, request.MoveFiles));
    }

    /// <summary>Renames the selected torrent: its name in the list and its file or folder on the server.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnOne))]
    private async Task RenameAsync()
    {
        if (_list.SelectedTorrents is not [var torrent]) return;

        var name = await _dialogs.PromptAsync(NameRules.RenamePrompt("torrent", torrent.Name));
        if (name is null) return;

        await RunAsync("Couldn't rename the torrent", [torrent.Hash],
            _ => _actions.RenamePathAsync(torrent.Hash, torrent.Name, name));
    }

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private async Task RemoveAsync()
    {
        var selected = _list.SelectedTorrents;
        if (selected.Count == 0) return;

        var request = await _dialogs.ConfirmRemoveAsync(selected.Select(t => t.Name).ToList());
        if (request is null) return;

        await RunAsync("Couldn't remove the torrents", selected.Select(t => t.Hash).ToList(),
            hashes => _actions.RemoveAsync(hashes, request.DeleteLocalData));
    }

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task MoveToTopAsync() => MoveAsync(QueueMove.Top);

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task MoveUpAsync() => MoveAsync(QueueMove.Up);

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task MoveDownAsync() => MoveAsync(QueueMove.Down);

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task MoveToBottomAsync() => MoveAsync(QueueMove.Bottom);

    [RelayCommand(CanExecute = nameof(CanChangeSettings))]
    private async Task SpeedLimitsAsync()
    {
        try
        {
            var updated = await _dialogs.ShowSpeedLimitsAsync(ct => _speedLimits.GetAsync(ct));
            if (updated is null) return;

            await _speedLimits.SetAsync(updated);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _dialogs.ShowMessageAsync("Couldn't change the speed limits", ex.Message);
        }
    }

    private Task MoveAsync(QueueMove move)
        => RunAsync("Couldn't change the queue order", SelectedHashes(), hashes => _actions.MoveInQueueAsync(hashes, move));

    private async Task RunAddAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _dialogs.ShowMessageAsync("Couldn't add the torrent", ex.Message);
        }
    }

    private IReadOnlyList<string> SelectedHashes() => _list.SelectedTorrents.Select(t => t.Hash).ToList();

    private async Task RunAsync(string failureTitle, IReadOnlyList<string> hashes, Func<IReadOnlyList<string>, Task> action)
    {
        if (hashes.Count == 0) return;

        try
        {
            await action(hashes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _dialogs.ShowMessageAsync(failureTitle, ex.Message);
        }
    }
}
