using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Synchro.Desktop.Controls;

/// <summary>
/// What every table in the app does with its columns, attached to a <see cref="DataGrid"/>:
/// <list type="bullet">
/// <item>a header click sorts through the view model (<c>sortBy</c> with the column's key), never the grid itself;</item>
/// <item>a double-click on the separator between two headers fits the column to its left to its content;</item>
/// <item>a right-click on a header offers Fit this column, Fit all columns and Reset column widths;</item>
/// <item>the widths can be read and applied by key, to remember them.</item>
/// </list>
/// A column's key is its <see cref="DataGridColumn.Tag"/>; its header is a <see cref="SortHeader"/> (or any content: the
/// header doesn't expose its column, so it is found by that content). Template columns need a SortMemberPath, or the
/// grid never raises Sorting for them.
/// </summary>
public sealed class GridColumns
{
    /// <summary>How close to a header's edge a double-click counts as "on the separator", in pixels.</summary>
    private const double SeparatorReach = 6;

    /// <summary>Room left of and right of the content, so fitted text doesn't touch the column edge.</summary>
    private const double FitSlack = 4;

    private readonly DataGrid _grid;
    private readonly Action<string>? _sortBy;
    private readonly Dictionary<DataGridColumn, DataGridLength> _defaultWidths;

    /// <param name="sortBy">Called with a column's key when its header is clicked; null for a table that doesn't sort.</param>
    public GridColumns(DataGrid grid, Action<string>? sortBy)
    {
        _grid = grid;
        _sortBy = sortBy;
        _defaultWidths = grid.Columns.ToDictionary(c => c, c => c.Width);

        grid.Sorting += OnSorting;
        // handledEventsToo: the header may already have handled the taps as the start of a resize.
        grid.AddHandler(InputElement.DoubleTappedEvent, OnDoubleTapped, RoutingStrategies.Bubble, handledEventsToo: true);
        // The header's own menu wins over the grid's row menu, because it is nearer to the click.
        grid.Styles.Add(new Style(x => x.OfType<DataGridColumnHeader>())
        {
            Setters = { new Setter(Control.ContextMenuProperty, new FuncTemplate<ContextMenu>(CreateHeaderMenu)) },
        });
    }

    public static string KeyOf(DataGridColumn column) => Convert.ToString(column.Tag, CultureInfo.InvariantCulture) ?? "";

    /// <summary>The widths the user set, by key; a star column (filling the rest) only once it was fitted.</summary>
    public IReadOnlyDictionary<string, double> CurrentWidths() =>
        _grid.Columns
            .Where(c => c.Tag is not null && !c.Width.IsStar && c.ActualWidth > 0)
            .ToDictionary(KeyOf, c => Math.Round(c.ActualWidth));

    public void ApplyWidths(IReadOnlyDictionary<string, double>? widths)
    {
        if (widths is null) return;

        foreach (var column in _grid.Columns)
        {
            if (column.Tag is not null && widths.TryGetValue(KeyOf(column), out var width))
                column.Width = new DataGridLength(Math.Max(width, column.MinWidth));
        }
    }

    public void ResetWidths()
    {
        foreach (var (column, width) in _defaultWidths)
            column.Width = width;
    }

    public void FitAll()
    {
        foreach (var column in _grid.Columns.Where(c => c.IsVisible).ToList())
            FitToContent(column);
    }

    /// <summary>
    /// Sets the column to the width its header and the rows on screen need without shortening anything (the grid only
    /// creates those rows). The grid's own Auto width measures cells at their current width, where text that is
    /// shortened with "…" already "fits", so the content is measured here with unlimited room instead.
    /// </summary>
    public void FitToContent(DataGridColumn column)
    {
        if (HeaderOf(column) is not { } header) return;

        var needed = Natural(header.Content as Control) + header.Padding.Left + header.Padding.Right;

        // A cell lies straight under its column's header.
        var left = header.TranslatePoint(default, _grid)?.X ?? double.NaN;
        foreach (var cell in _grid.GetVisualDescendants().OfType<DataGridCell>())
        {
            if (cell.TranslatePoint(default, _grid) is not { } at || Math.Abs(at.X - left) > 1) continue;
            needed = Math.Max(needed, Natural(cell.Content as Control) + cell.Padding.Left + cell.Padding.Right);
        }

        column.Width = new DataGridLength(Math.Ceiling(Math.Clamp(needed + FitSlack, column.MinWidth, column.MaxWidth)));
    }

    // A header click: the view model sorts (it keeps the order through every poll and remembers it), not the grid.
    private void OnSorting(object? sender, DataGridColumnEventArgs e)
    {
        e.Handled = true;
        if (_sortBy is not null && e.Column.Tag is not null)
            _sortBy(KeyOf(e.Column));
    }

    // A double-click on the separator between two headers fits the column to its left, as in Excel, the Finder and
    // Windows Explorer.
    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true) is not { } header) return;
        if (ColumnOf(header) is not { } column) return;

        var x = e.GetPosition(header).X;
        var target = x >= header.Bounds.Width - SeparatorReach ? column
            : x <= SeparatorReach ? PreviousColumn(column)
            : null;
        if (target is null) return;

        FitToContent(target);
        e.Handled = true;
    }

    // One menu per header; a menu's logical parent is the header it belongs to.
    private ContextMenu CreateHeaderMenu()
    {
        var menu = new ContextMenu();
        var fitColumn = new MenuItem { Header = "Fit this column" };
        fitColumn.Click += (_, _) =>
        {
            if (menu.FindLogicalAncestorOfType<DataGridColumnHeader>() is { } header && ColumnOf(header) is { } column)
                FitToContent(column);
        };
        var fitAll = new MenuItem { Header = "Fit all columns" };
        fitAll.Click += (_, _) => FitAll();
        var reset = new MenuItem { Header = "Reset column widths" };
        reset.Click += (_, _) => ResetWidths();

        menu.Items.Add(fitColumn);
        menu.Items.Add(fitAll);
        menu.Items.Add(new Separator());
        menu.Items.Add(reset);
        return menu;
    }

    private DataGridColumn? ColumnOf(DataGridColumnHeader header) =>
        _grid.Columns.FirstOrDefault(c => ReferenceEquals(c.Header, header.Content));

    private DataGridColumnHeader? HeaderOf(DataGridColumn column) =>
        _grid.GetVisualDescendants().OfType<DataGridColumnHeader>().FirstOrDefault(h => ReferenceEquals(h.Content, column.Header));

    private DataGridColumn? PreviousColumn(DataGridColumn column) =>
        _grid.Columns.Where(c => c.IsVisible && c.DisplayIndex < column.DisplayIndex).MaxBy(c => c.DisplayIndex);

    /// <summary>The width a control asks for when nothing limits it: full text, nothing shortened.</summary>
    private static double Natural(Control? content)
    {
        if (content is null) return 0;

        content.Measure(Size.Infinity);
        var width = content.DesiredSize.Width;
        content.InvalidateMeasure(); // the next layout pass measures it again at the real width
        return width;
    }
}
