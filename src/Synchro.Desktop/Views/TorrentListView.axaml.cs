using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Synchro.Core.Settings;
using Synchro.ViewModels;

namespace Synchro.Desktop.Views;

public partial class TorrentListView : UserControl
{
    private MainWindowViewModel? _main;
    private TorrentListViewModel? _list;
    private bool _syncingSelection;

    // The widths from the XAML, for Reset column widths.
    private readonly Dictionary<DataGridColumn, DataGridLength> _defaultWidths;

    public TorrentListView()
    {
        InitializeComponent();
        _defaultWidths = TorrentGrid.Columns.ToDictionary(c => c, c => c.Width);
        TorrentGrid.SelectionChanged += OnSelectionChanged;
        // handledEventsToo: the header may already have handled the taps as the start of a resize.
        TorrentGrid.AddHandler(DoubleTappedEvent, OnGridDoubleTapped, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>How close to a header's edge a double-click counts as "on the separator", in pixels.</summary>
    private const double SeparatorReach = 6;

    // A double-click on the separator between two headers fits the column to its left to its content, as in Excel,
    // the Finder and Windows Explorer. The Name column too: it then gets a fixed width like the others.
    private void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true) is not { } header) return;
        var column = TorrentGrid.Columns.FirstOrDefault(c => ReferenceEquals(c.Header, header.Content));
        if (column is null) return;

        var x = e.GetPosition(header).X;
        var target = x >= header.Bounds.Width - SeparatorReach ? column
            : x <= SeparatorReach ? PreviousColumn(column)
            : null;
        if (target is null) return;

        FitToContent(target);
        e.Handled = true;
    }

    // The header menu (a right-click on a column header). The menu's logical parent is the header it opened on.
    private void OnFitColumnClick(object? sender, RoutedEventArgs e)
    {
        if (ColumnOf(sender) is { } column) FitToContent(column);
    }

    private void OnFitAllColumnsClick(object? sender, RoutedEventArgs e)
    {
        foreach (var column in TorrentGrid.Columns.Where(c => c.IsVisible).ToList())
            FitToContent(column);
    }

    private void OnResetColumnWidthsClick(object? sender, RoutedEventArgs e)
    {
        foreach (var (column, width) in _defaultWidths)
            column.Width = width;
    }

    private DataGridColumn? ColumnOf(object? menuItem) =>
        (menuItem as ILogical)?.FindLogicalAncestorOfType<DataGridColumnHeader>() is { } header
            ? TorrentGrid.Columns.FirstOrDefault(c => ReferenceEquals(c.Header, header.Content))
            : null;

    private DataGridColumn? PreviousColumn(DataGridColumn column) =>
        TorrentGrid.Columns.Where(c => c.IsVisible && c.DisplayIndex < column.DisplayIndex).MaxBy(c => c.DisplayIndex);

    /// <summary>Room left of and right of the content, so fitted text doesn't touch the column edge.</summary>
    private const double FitSlack = 4;

    /// <summary>
    /// Sets the column to the width its header and the rows on screen need without shortening anything (the grid only
    /// creates those rows). The grid's own Auto width measures cells at their current width, where text that is
    /// shortened with "…" already "fits", so the content is measured here with unlimited room instead.
    /// </summary>
    private void FitToContent(DataGridColumn column)
    {
        if (TorrentGrid.GetVisualDescendants().OfType<DataGridColumnHeader>()
                .FirstOrDefault(h => ReferenceEquals(h.Content, column.Header)) is not { } header)
            return;

        var needed = Natural(header.Content as Control) + header.Padding.Left + header.Padding.Right;

        // A cell lies straight under its column's header.
        var left = header.TranslatePoint(default, TorrentGrid)?.X ?? double.NaN;
        foreach (var cell in TorrentGrid.GetVisualDescendants().OfType<DataGridCell>())
        {
            if (cell.TranslatePoint(default, TorrentGrid) is not { } at || Math.Abs(at.X - left) > 1) continue;
            needed = Math.Max(needed, Natural(cell.Content as Control) + cell.Padding.Left + cell.Padding.Right);
        }

        column.Width = new DataGridLength(Math.Ceiling(Math.Clamp(needed + FitSlack, column.MinWidth, column.MaxWidth)));
    }

    /// <summary>The width a control asks for when nothing limits it: full text, nothing shortened.</summary>
    private static double Natural(Control? content)
    {
        if (content is null) return 0;

        content.Measure(Size.Infinity);
        var width = content.DesiredSize.Width;
        content.InvalidateMeasure(); // the next layout pass measures it again at the real width
        return width;
    }

    // A header click: the view model sorts (it keeps the order through every poll and remembers it), not the grid.
    private void OnSorting(object? sender, DataGridColumnEventArgs e)
    {
        e.Handled = true;
        if (e.Column.Tag is TorrentSortColumn column)
            _list?.SortByCommand.Execute(column);
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
        TorrentGrid.Columns
            .Where(c => c.Tag is TorrentSortColumn && !c.Width.IsStar && c.ActualWidth > 0)
            .ToDictionary(c => (TorrentSortColumn)c.Tag!, c => Math.Round(c.ActualWidth));

    // The saved widths arrive once start-up has read the settings.
    private void OnMainPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ColumnWidths))
            ApplyColumnWidths(_main?.ColumnWidths);
    }

    private void ApplyColumnWidths(IReadOnlyDictionary<TorrentSortColumn, double>? widths)
    {
        if (widths is null) return;

        foreach (var column in TorrentGrid.Columns)
        {
            if (column.Tag is TorrentSortColumn key && widths.TryGetValue(key, out var width))
                column.Width = new DataGridLength(Math.Max(width, column.MinWidth));
        }
    }

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
