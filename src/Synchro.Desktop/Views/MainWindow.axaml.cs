using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Synchro.Core.Torrents;
using Synchro.Desktop.Services;
using Synchro.ViewModels;

namespace Synchro.Desktop.Views;

public partial class MainWindow : Window
{
    private TorrentListViewModel? _list;
    private bool _syncingSelection;

    public MainWindow()
    {
        InitializeComponent();

        TorrentListBox.SelectionChanged += OnSelectionChanged;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Start-up work needs the window to exist (first run opens a dialog on top of it).
        Opened += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                await vm.InitializeCommand.ExecuteAsync(null);
        };
    }

    // The server menu is a Flyout (it holds a list), which doesn't close by itself on a click like a menu does.
    // Close it after the click: a button runs its command after the Click event, and closing the flyout first
    // detaches the button, which clears its command binding, so nothing would happen.
    private void OnServerMenuItemClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Avalonia.Threading.Dispatcher.UIThread.Post(() => ServerMenuButton.Flyout?.Hide());

    // Torrent > Exit (Windows and Linux; macOS quits from the app menu).
    private void OnExitMenuClick(object? sender, EventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.TryShutdown();
    }

    // View > Search (Cmd+F): focus is a view concern, so this stays in code-behind.
    private void OnSearchMenuClick(object? sender, EventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_list is not null)
            _list.SelectionRestoreRequested -= OnSelectionRestoreRequested;

        _list = (DataContext as MainWindowViewModel)?.TorrentList;

        if (_list is not null)
            _list.SelectionRestoreRequested += OnSelectionRestoreRequested;
    }

    // The user changed the selection: tell the view model (it owns the selection, by hash).
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || _list is null) return;

        _list.SetSelection(TorrentListBox.SelectedItems?.OfType<TorrentItemViewModel>() ?? []);
    }

    // The view model reordered or rebuilt the rows: put the selection back on the right rows.
    private void OnSelectionRestoreRequested(object? sender, EventArgs e)
    {
        if (_list is null || TorrentListBox.SelectedItems is not { } selected) return;

        _syncingSelection = true;
        try
        {
            selected.Clear();
            foreach (var item in _list.VisibleTorrents)
            {
                if (_list.IsSelected(item.Hash))
                    selected.Add(item);
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        var acceptable = e.DataTransfer.Contains(DataFormat.File) || e.DataTransfer.Contains(DataFormat.Text);
        e.DragEffects = acceptable ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    // Torrent files and magnet links (or URLs) can be dropped anywhere on the window.
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not MainWindowViewModel vm) return;

        // Read everything from the drag payload before the first await: the platform may invalidate it afterwards.
        List<IStorageFile> files = [];
        string? text = null;
        try
        {
            files = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>()
                .Where(f => f.Name.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                .ToList() ?? [];
            text = e.DataTransfer.TryGetText();
        }
        catch (Exception)
        {
            // A malformed drag payload must never take the app down; carry on with what we have.
        }

        var sources = new List<AddTorrentSource>();
        foreach (var file in files)
        {
            try { sources.Add(await TorrentFileReader.ReadAsync(file)); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                // An unreadable or oversized file is skipped; the rest of the drop still goes through.
            }
        }

        if (AddTorrentSource.TryParseLink(text, out var link))
            sources.Add(link);

        await vm.Toolbar.AddSourcesAsync(sources);
    }
}
