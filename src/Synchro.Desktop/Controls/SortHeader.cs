using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Material.Icons;
using Material.Icons.Avalonia;
using Synchro.Core.Settings;

namespace Synchro.Desktop.Controls;

/// <summary>
/// A torrent list column header: its title and, while the list is sorted by this column, an arrow for the direction.
/// Numeric columns are right-aligned with the arrow before the title, so the titles line up with the numbers below.
/// </summary>
public sealed class SortHeader : StackPanel
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<SortHeader, string>(nameof(Text), "");

    public static readonly StyledProperty<TorrentSortColumn> ColumnProperty =
        AvaloniaProperty.Register<SortHeader, TorrentSortColumn>(nameof(Column));

    public static readonly StyledProperty<TorrentSort?> SortProperty =
        AvaloniaProperty.Register<SortHeader, TorrentSort?>(nameof(Sort));

    public static readonly StyledProperty<bool> IsNumericProperty =
        AvaloniaProperty.Register<SortHeader, bool>(nameof(IsNumeric));

    private readonly TextBlock _title = new() { Classes = { "header" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly MaterialIcon _arrow = new() { Width = 16, Height = 16, IsVisible = false };

    public SortHeader()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 2;
        _arrow[!MaterialIcon.ForegroundProperty] = _arrow.GetResourceObservable("MaterialBodyLightBrush").ToBinding();
        Children.Add(_title);
        Children.Add(_arrow);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public TorrentSortColumn Column
    {
        get => GetValue(ColumnProperty);
        set => SetValue(ColumnProperty, value);
    }

    /// <summary>The list's current sort, bound from the view model.</summary>
    public TorrentSort? Sort
    {
        get => GetValue(SortProperty);
        set => SetValue(SortProperty, value);
    }

    /// <summary>Right-aligned, arrow first.</summary>
    public bool IsNumeric
    {
        get => GetValue(IsNumericProperty);
        set => SetValue(IsNumericProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            _title.Text = Text;
        }
        else if (change.Property == IsNumericProperty)
        {
            HorizontalAlignment = IsNumeric ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            Children.Clear();
            Children.AddRange(IsNumeric ? [_arrow, _title] : [_title, _arrow]);
        }
        else if (change.Property == SortProperty || change.Property == ColumnProperty)
        {
            _arrow.IsVisible = Sort?.Column == Column;
            _arrow.Kind = Sort is { Descending: true } ? MaterialIconKind.MenuDown : MaterialIconKind.MenuUp;
        }
    }
}
