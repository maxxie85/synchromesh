using Avalonia.Controls;
using Avalonia.Data;
using Synchro.Desktop.Controls;
using Synchro.ViewModels.Details;

namespace Synchro.Desktop.Views;

public partial class FileTreeView : UserControl
{
    public FileTreeView()
    {
        InitializeComponent();
        Columns = new GridColumns(FileGrid, key => ViewModel?.SortByCommand.Execute(key));
        FileGrid.LoadingRow += OnLoadingRow;
    }

    /// <summary>The table's columns: their widths are remembered with the other tables'.</summary>
    public GridColumns Columns { get; }

    private FilesViewModel? ViewModel => DataContext as FilesViewModel;

    // Done and Progress only mean something once the torrent is on the server (not in the Add dialog). Columns aren't
    // in the visual tree, so they can't bind to the view model themselves.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        var showsProgress = ViewModel?.ShowsProgress ?? true;
        foreach (var column in FileGrid.Columns)
        {
            if (GridColumns.KeyOf(column) is FilesViewModel.Columns.Done or FilesViewModel.Columns.Progress)
                column.IsVisible = showsProgress;
        }
    }

    // A right-click on a row offers Rename…; files chosen in the Add dialog can't be renamed, so no menu there.
    private void OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (ViewModel is not { CanRename: true })
        {
            e.Row.ContextMenu = null;
            return;
        }

        if (e.Row.ContextMenu is not null) return; // rows are reused; the menu follows the row's data
        var rename = new MenuItem { Header = "Rename…" };
        rename.Bind(MenuItem.CommandProperty, new Binding(nameof(FileNodeViewModel.RenameCommand)));
        e.Row.ContextMenu = new ContextMenu { Items = { rename } };
    }
}
