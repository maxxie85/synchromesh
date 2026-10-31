using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Synchro.Core.Torrents;
using Synchro.Desktop.Services;
using Synchro.ViewModels;

namespace Synchro.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Remember the list's column widths. Waits for the small write (the store doesn't need the UI thread), so
        // they're saved even when the app quits right after.
        Closing += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                vm.SaveColumnWidthsAsync(TorrentListPanel.CurrentColumnWidths()).GetAwaiter().GetResult();
        };

        // Start-up work needs the window to exist (first run opens a dialog on top of it).
        Opened += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                await vm.InitializeCommand.ExecuteAsync(null);
        };
    }

    // Torrent > Exit (Windows and Linux; macOS quits from the app menu).
    private void OnExitMenuClick(object? sender, EventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.TryShutdown();
    }

    // View > Search (Cmd+F): focus is a view concern; the search box lives in the toolbar.
    private void OnSearchMenuClick(object? sender, EventArgs e) => ToolbarPanel.FocusSearch();

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
