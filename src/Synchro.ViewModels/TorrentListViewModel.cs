using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Settings;
using Synchro.Core.Torrents;

namespace Synchro.ViewModels;

/// <summary>
/// Holds every torrent of the connected server, keyed by info-hash (ids are only stable while the
/// daemon runs), and exposes the filtered, searched slice the list view shows, sorted by <see cref="Sort"/> (queue
/// order unless the user clicked a column header). Rows whose sort value changed move in place after each poll.
/// It also owns the selection, so it survives rows being reordered or rebuilt.
/// Updates arrive on the UI context, so no locking is needed.
/// </summary>
public sealed partial class TorrentListViewModel : ObservableObject, IDisposable
{
    private readonly ITorrentFeed _feed;
    private readonly IConnectionService _connection;
    private readonly Dictionary<string, TorrentItemViewModel> _byHash = [];
    private readonly Dictionary<int, string> _hashById = [];
    private readonly HashSet<string> _visible = [];

    // Selection is stored by hash. The view reports what the user selects; while this view model
    // mutates VisibleTorrents the list control fires selection events for rows that merely moved,
    // so those are ignored and the view is asked to restore the selection afterwards.
    private HashSet<string> _selected = [];
    private bool _selectionSyncSuppressed;
    private bool _restoreNeeded;
    private bool _selectionChangedPending;

    // The first full refresh after connecting selects the top row, so the details panel has something to show.
    private bool _selectTopOnFirstLoad = true;

    private Comparison<TorrentItemViewModel> _order = TorrentSorting.Comparison(TorrentSort.Default);

    public TorrentListViewModel(ITorrentFeed feed, IConnectionService connection)
    {
        _feed = feed;
        _connection = connection;

        Filters =
        [
            new FilterItemViewModel(TorrentFilter.All, "All"),
            new FilterItemViewModel(TorrentFilter.Downloading, "Downloading"),
            new FilterItemViewModel(TorrentFilter.Seeding, "Seeding"),
            new FilterItemViewModel(TorrentFilter.Paused, "Paused"),
            new FilterItemViewModel(TorrentFilter.Errors, "Errors"),
        ];
        _selectedFilter = Filters[0];
        _selectedFilter.IsSelected = true;
        _selectedLabel = LabelFilters[0];
        UpdateEmptyMessage();

        _feed.TorrentsUpdated += OnTorrentsUpdated;
        _connection.StatusChanged += OnConnectionStatusChanged;
    }

    public BulkObservableCollection<TorrentItemViewModel> VisibleTorrents { get; } = [];
    public IReadOnlyList<FilterItemViewModel> Filters { get; }

    /// <summary>The selected, still visible torrents in queue order.</summary>
    public IReadOnlyList<TorrentItemViewModel> SelectedTorrents =>
        _selected.Select(h => _byHash.GetValueOrDefault(h))
            .OfType<TorrentItemViewModel>()
            .OrderBy(t => t.Torrent.QueuePosition)
            .ToList();

    /// <summary>Raised when the set of selected torrents changed.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Raised after rows were removed, moved or rebuilt. The view should re-select the rows for which
    /// <see cref="IsSelected"/> is true, without reporting that back as a user action.
    /// </summary>
    public event EventHandler? SelectionRestoreRequested;

    [ObservableProperty] private FilterItemViewModel _selectedFilter;

    /// <summary>The column and direction the list is sorted by; set from the saved settings at start-up.</summary>
    [ObservableProperty] private TorrentSort _sort = TorrentSort.Default;

    /// <summary>A click on a column header: that column, or the other direction when it is already sorted by it.</summary>
    [RelayCommand]
    private void SortBy(TorrentSortColumn column) => Sort = TorrentSorting.Toggle(Sort, column);

    partial void OnSortChanged(TorrentSort value)
    {
        _order = TorrentSorting.Comparison(value);
        BeginBatch();
        try
        {
            Reorder();
        }
        finally
        {
            EndBatch();
        }
    }

    /// <summary>"All labels" first, then every label in use, by name. Combined with the status filter.</summary>
    public ObservableCollection<LabelFilterItemViewModel> LabelFilters { get; } = [new LabelFilterItemViewModel(null)];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLabelFilter))]
    private LabelFilterItemViewModel? _selectedLabel;

    public bool HasLabels => LabelFilters.Count > 1;
    public bool HasLabelFilter => SelectedLabel is { IsAll: false };

    /// <summary>Every label on any torrent of this server, by name: what the Labels dialog offers.</summary>
    public IReadOnlyList<string> KnownLabels => LabelFilters.Where(l => !l.IsAll).Select(l => l.Label!).ToList();
    [ObservableProperty] private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmptyMessage))]
    private string _emptyMessage = "";

    public bool HasEmptyMessage => !string.IsNullOrEmpty(EmptyMessage);

    public bool IsSelected(string hash) => _selected.Contains(hash);

    /// <summary>Called by the view when the user changes the selection.</summary>
    public void SetSelection(IEnumerable<TorrentItemViewModel> items)
    {
        if (_selectionSyncSuppressed) return;

        var hashes = items.Select(i => i.Hash).ToHashSet();
        if (hashes.SetEquals(_selected)) return;

        _selected = hashes;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _feed.TorrentsUpdated -= OnTorrentsUpdated;
        _connection.StatusChanged -= OnConnectionStatusChanged;
    }

    /// <summary>Shows only the torrents matching <paramref name="filter"/> (View menu, Cmd+1 to Cmd+5).</summary>
    [RelayCommand]
    private void SelectFilter(TorrentFilter filter) => SelectedFilter = Filters.First(f => f.Filter == filter);

    partial void OnSelectedFilterChanged(FilterItemViewModel value)
    {
        foreach (var f in Filters)
            f.IsSelected = ReferenceEquals(f, value);

        RebuildVisible();
    }
    partial void OnSearchTextChanged(string value) => RebuildVisible();
    partial void OnSelectedLabelChanged(LabelFilterItemViewModel? value) => RebuildVisible();

    private void OnTorrentsUpdated(object? sender, TorrentUpdate update) => Apply(update);

    private void OnConnectionStatusChanged(object? sender, EventArgs e)
    {
        // A new connection attempt starts from a clean slate. A transient drop (state Failed, then
        // Connected again) keeps the last known list on screen instead of blanking it.
        if (_connection.Status.State == ConnectionState.Connecting)
            Clear();
        else
            UpdateEmptyMessage();
    }

    private void Apply(TorrentUpdate update)
    {
        BeginBatch();
        try
        {
            if (update.IsFullRefresh)
            {
                var present = update.Torrents.Select(t => t.HashString).ToHashSet();
                foreach (var hash in _byHash.Keys.Where(h => !present.Contains(h)).ToList())
                    Remove(hash);
            }

            foreach (var id in update.RemovedIds)
            {
                if (_hashById.TryGetValue(id, out var hash))
                    Remove(hash);
            }

            foreach (var torrent in update.Torrents)
                Upsert(torrent);

            // Values the list is sorted by (queue positions, speeds, progress…) change with every poll: move the rows
            // that are now out of place. Inserting new rows already found their place.
            Reorder();

            if (update.IsFullRefresh && _selectTopOnFirstLoad)
            {
                _selectTopOnFirstLoad = false;
                if (_selected.Count == 0 && VisibleTorrents.Count > 0)
                    SelectTop();
            }

            UpdateCounts();
            UpdateEmptyMessage();
        }
        finally
        {
            EndBatch();
        }
    }

    private void SelectTop()
    {
        _selected = [VisibleTorrents[0].Hash];
        _selectionChangedPending = true;
        _restoreNeeded = true; // the view shows the selection when the batch ends
    }

    private void Upsert(Torrent torrent)
    {
        if (_byHash.TryGetValue(torrent.HashString, out var item))
        {
            _hashById.Remove(item.Id);
            item.Apply(torrent);
        }
        else
        {
            item = new TorrentItemViewModel(torrent);
            _byHash[torrent.HashString] = item;
        }

        _hashById[torrent.Id] = torrent.HashString;

        var shouldShow = IsVisible(item);
        var isShown = _visible.Contains(item.Hash);
        if (shouldShow && !isShown) InsertSorted(item);
        else if (!shouldShow && isShown) HideItem(item);
    }

    private void Remove(string hash)
    {
        if (!_byHash.Remove(hash, out var item)) return;

        _hashById.Remove(item.Id);
        HideItem(item);
        if (_selected.Remove(hash)) _selectionChangedPending = true;
    }

    private void Clear()
    {
        BeginBatch();
        try
        {
            _byHash.Clear();
            _hashById.Clear();
            _visible.Clear();
            VisibleTorrents.Clear();

            if (_selected.Count > 0)
            {
                _selected.Clear();
                _selectionChangedPending = true;
            }

            _restoreNeeded = true;
            _selectTopOnFirstLoad = true; // a new connection (or server) starts with its top row selected again
            UpdateCounts();
            UpdateEmptyMessage();
        }
        finally
        {
            EndBatch();
        }
    }

    private bool IsVisible(TorrentItemViewModel item) =>
        FilterItemViewModel.Matches(SelectedFilter?.Filter ?? TorrentFilter.All, item.Torrent)
        && (SelectedLabel is not { IsAll: false } label || item.Torrent.LabelList.Contains(label.Label!))
        && (string.IsNullOrWhiteSpace(SearchText)
            || item.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));

    private void InsertSorted(TorrentItemViewModel item)
    {
        var index = 0;
        while (index < VisibleTorrents.Count && _order(VisibleTorrents[index], item) <= 0)
            index++;

        VisibleTorrents.Insert(index, item);
        _visible.Add(item.Hash);
    }

    private void HideItem(TorrentItemViewModel item)
    {
        if (!_visible.Remove(item.Hash)) return;

        VisibleTorrents.Remove(item);
        if (_selected.Remove(item.Hash)) _selectionChangedPending = true;
        _restoreNeeded = true;
    }

    /// <summary>Filter or search changed.</summary>
    private void RebuildVisible()
    {
        BeginBatch();
        try
        {
            RebuildVisibleCore();
            UpdateEmptyMessage();
        }
        finally
        {
            EndBatch();
        }
    }

    private void RebuildVisibleCore()
    {
        VisibleTorrents.Clear();
        _visible.Clear();

        var items = _byHash.Values.Where(IsVisible).ToList();
        items.Sort(_order);
        foreach (var item in items)
        {
            VisibleTorrents.Add(item);
            _visible.Add(item.Hash);
        }

        // Rows that dropped out of the filter are no longer selected.
        if (_selected.RemoveWhere(h => !_visible.Contains(h)) > 0)
            _selectionChangedPending = true;

        _restoreNeeded = true;
    }

    /// <summary>A reorder with more rows out of place than this resets the list in one go instead of row by row.</summary>
    internal const int MaxSingleMoves = 16;

    /// <summary>
    /// Brings the visible rows into <see cref="Sort"/> order. After a poll usually only a few rows are out of place
    /// (a speed changed): those are removed and inserted again, so the list keeps its other rows and its scroll
    /// position. A new sort moves nearly everything: one Reset then. Never Move: the DataGrid's collection view ignores
    /// Move notifications, so it would keep drawing the old order.
    /// </summary>
    private void Reorder()
    {
        var target = VisibleTorrents.ToList();
        target.Sort(_order);

        // The steps needed, worked out on a copy: one row that overtakes the others is one step, even though every row
        // below it shifts a place.
        var steps = MoveSteps(VisibleTorrents.ToList(), target);
        if (steps is { Count: 0 }) return;

        if (steps is null)
        {
            VisibleTorrents.ReplaceAll(target);
        }
        else
        {
            foreach (var (from, to) in steps)
            {
                var item = VisibleTorrents[from];
                VisibleTorrents.RemoveAt(from);
                VisibleTorrents.Insert(to, item);
            }
        }

        _restoreNeeded = true;
    }

    /// <summary>The remove-and-insert steps that turn <paramref name="current"/> into <paramref name="target"/>, or null
    /// when there are more than <see cref="MaxSingleMoves"/>.</summary>
    private static List<(int From, int To)>? MoveSteps(List<TorrentItemViewModel> current, List<TorrentItemViewModel> target)
    {
        var steps = new List<(int, int)>();
        for (var i = 0; i < target.Count; i++)
        {
            if (ReferenceEquals(current[i], target[i])) continue;
            if (steps.Count == MaxSingleMoves) return null;

            var from = i + 1;
            while (!ReferenceEquals(current[from], target[i])) from++;
            var item = current[from];
            current.RemoveAt(from);
            current.Insert(i, item);
            steps.Add((from, i));
        }

        return steps;
    }

    private void BeginBatch() => _selectionSyncSuppressed = true;

    private void EndBatch()
    {
        _selectionSyncSuppressed = false;

        if (_restoreNeeded)
        {
            _restoreNeeded = false;
            SelectionRestoreRequested?.Invoke(this, EventArgs.Empty);
        }

        if (_selectionChangedPending)
        {
            _selectionChangedPending = false;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateCounts()
    {
        foreach (var filter in Filters)
            filter.Count = _byHash.Values.Count(i => FilterItemViewModel.Matches(filter.Filter, i.Torrent));
        UpdateLabelFilters();
    }

    // Keeps the label list in step with the torrents, reusing rows so the sidebar selection stays put.
    private void UpdateLabelFilters()
    {
        var counts = _byHash.Values
            .SelectMany(i => i.Torrent.LabelList.Distinct())
            .GroupBy(l => l, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var names = counts.Keys.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

        LabelFilters[0].Count = _byHash.Count;
        var current = LabelFilters.Skip(1).Select(l => l.Label!).ToList();
        if (!current.SequenceEqual(names))
        {
            var existing = LabelFilters.Skip(1).ToDictionary(l => l.Label!, StringComparer.Ordinal);
            while (LabelFilters.Count > 1) LabelFilters.RemoveAt(1);
            foreach (var name in names)
                LabelFilters.Add(existing.GetValueOrDefault(name) ?? new LabelFilterItemViewModel(name));
            OnPropertyChanged(nameof(HasLabels));
            OnPropertyChanged(nameof(KnownLabels));

            // The chosen label is gone (no torrent has it any more): show all again.
            if (SelectedLabel is { IsAll: false } selected && !counts.ContainsKey(selected.Label!))
                SelectedLabel = LabelFilters[0];
        }

        foreach (var item in LabelFilters.Skip(1))
            item.Count = counts[item.Label!];
    }

    private void UpdateEmptyMessage()
    {
        if (VisibleTorrents.Count > 0)
            EmptyMessage = "";
        else if (_connection.Status.State is not (ConnectionState.Connected or ConnectionState.Failed) && _byHash.Count == 0)
            EmptyMessage = "Connect to a server to see its torrents.";
        else if (_byHash.Count == 0)
            EmptyMessage = _connection.Status.State == ConnectionState.Connected
                ? "There are no torrents on this server yet."
                : "";
        else
            EmptyMessage = HasLabelFilter ? "No torrents match this filter and label." : "No torrents match this filter.";
    }
}
