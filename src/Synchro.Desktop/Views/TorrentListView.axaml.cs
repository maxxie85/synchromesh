using Avalonia.Controls;
using Synchro.ViewModels;

namespace Synchro.Desktop.Views;

public partial class TorrentListView : UserControl
{
    private TorrentListViewModel? _list;
    private bool _syncingSelection;

    public TorrentListView()
    {
        InitializeComponent();
        TorrentListBox.SelectionChanged += OnSelectionChanged;
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
}
