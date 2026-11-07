using Avalonia.Controls;
using Synchro.Core.Settings;
using Synchro.Desktop.Controls;
using Synchro.ViewModels;

namespace Synchro.Desktop.Views;

public partial class TorrentListView : UserControl
{
    private MainWindowViewModel? _main;
    private TorrentListViewModel? _list;
    private bool _syncingSelection;

    private readonly GridColumns _columns;

    public TorrentListView()
    {
        InitializeComponent();
        TorrentGrid.SelectionChanged += OnSelectionChanged;
        _columns = new GridColumns(TorrentGrid,
            key => _list?.SortByCommand.Execute(Enum.Parse<TorrentSortColumn>(key)));
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_list is not null)
            _list.SelectionRestoreRequested -= OnSelectionRestoreRequested;
        if (_main is not null)
            _main.PropertyChanged -= OnMainPropertyChanged;

        _main = DataContext as MainWindowViewModel;
        _list = _main?.TorrentList;

        if (_list is not null)
            _list.SelectionRestoreRequested += OnSelectionRestoreRequested;
        if (_main is not null)
        {
            _main.PropertyChanged += OnMainPropertyChanged;
            ApplyColumnWidths(_main.ColumnWidths);
        }
    }

    /// <summary>The widths the user set; the Name column only once it was fitted (until then it fills the rest).</summary>
    public IReadOnlyDictionary<TorrentSortColumn, double> CurrentColumnWidths() =>
        _columns.CurrentWidths().ToDictionary(w => Enum.Parse<TorrentSortColumn>(w.Key), w => w.Value);

    // The saved widths arrive once start-up has read the settings.
    private void OnMainPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ColumnWidths))
            ApplyColumnWidths(_main?.ColumnWidths);
    }

    private void ApplyColumnWidths(IReadOnlyDictionary<TorrentSortColumn, double>? widths) =>
        _columns.ApplyWidths(widths?.ToDictionary(w => w.Key.ToString(), w => w.Value));

    // The user changed the selection: tell the view model (it owns the selection, by hash).
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || _list is null) return;

        _list.SetSelection(TorrentGrid.SelectedItems?.OfType<TorrentItemViewModel>() ?? []);
    }

    // The view model reordered or rebuilt the rows: put the selection back on the right rows.
    private void OnSelectionRestoreRequested(object? sender, EventArgs e)
    {
        if (_list is null || TorrentGrid.SelectedItems is not { } selected) return;

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
