using System.ComponentModel;
using Avalonia.Controls;
using Synchro.Desktop.Controls;
using Synchro.ViewModels.Details;

namespace Synchro.Desktop.Views;

public partial class TorrentDetailsView : UserControl
{
    private readonly GridColumns _trackerColumns;
    private readonly GridColumns _peerColumns;
    private TorrentDetailsViewModel? _viewModel;

    public TorrentDetailsView()
    {
        InitializeComponent();
        _trackerColumns = new GridColumns(TrackerGrid, key => _viewModel?.SortTrackersCommand.Execute(key));
        _peerColumns = new GridColumns(PeerGrid, key => _viewModel?.SortPeersCommand.Execute(key));
    }

    /// <summary>The widths the user set in each table, by table key (as the settings keep them).</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> CurrentColumnWidths() =>
        new Dictionary<string, IReadOnlyDictionary<string, double>>
        {
            [TorrentDetailsViewModel.Tables.Trackers] = _trackerColumns.CurrentWidths(),
            [TorrentDetailsViewModel.Tables.Peers] = _peerColumns.CurrentWidths(),
            [TorrentDetailsViewModel.Tables.Files] = FileTree.Columns.CurrentWidths(),
        };

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as TorrentDetailsViewModel;
        if (_viewModel is null) return;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyColumnWidths();
    }

    // The saved widths arrive once start-up has read the settings.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TorrentDetailsViewModel.ColumnWidths))
            ApplyColumnWidths();
    }

    private void ApplyColumnWidths()
    {
        if (_viewModel?.ColumnWidths is not { } widths) return;

        _trackerColumns.ApplyWidths(widths.GetValueOrDefault(TorrentDetailsViewModel.Tables.Trackers));
        _peerColumns.ApplyWidths(widths.GetValueOrDefault(TorrentDetailsViewModel.Tables.Peers));
        FileTree.Columns.ApplyWidths(widths.GetValueOrDefault(TorrentDetailsViewModel.Tables.Files));
    }
}
