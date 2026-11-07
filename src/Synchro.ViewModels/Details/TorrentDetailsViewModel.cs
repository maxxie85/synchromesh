using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Synchro.AppServices.Torrents;
using Synchro.Core.Settings;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Services;

namespace Synchro.ViewModels.Details;

/// <summary>
/// The details panel under the list. It follows the list selection: with exactly one torrent selected it asks the
/// poller for that torrent's details and keeps the tabs up to date; otherwise it shows a short explanation.
/// </summary>
public sealed partial class TorrentDetailsViewModel : ObservableObject, IDisposable
{
    public const string NothingSelectedMessage = "Select a torrent to see its details.";
    public const string GoneMessage = "This torrent is no longer on the server.";

    /// <summary>Tab order in the view: General, Trackers, Peers, Files, Options.</summary>
    public const int FilesTabIndex = 3;
    public const int OptionsTabIndex = 4;

    private readonly ITorrentDetailsFeed _feed;
    private readonly TorrentListViewModel _list;
    private readonly TimeProvider _time;
    private readonly ITorrentActions _actions;
    private readonly IDialogService _dialogs;
    private string? _hash;
    private string _name = "";

    public TorrentDetailsViewModel(
        ITorrentDetailsFeed feed,
        TorrentListViewModel list,
        TimeProvider time,
        ITorrentActions actions,
        IDialogService dialogs)
    {
        _feed = feed;
        _list = list;
        _time = time;
        _actions = actions;
        _dialogs = dialogs;
        Files = new FilesViewModel(actions, dialogs);
        Options = new TorrentOptionsViewModel(actions, dialogs);

        _list.SelectionChanged += OnSelectionChanged;
        _feed.DetailsUpdated += OnDetailsUpdated;
    }

    public GeneralDetailsViewModel General { get; } = new();
    /// <summary>In <see cref="TrackerSort"/> order; the server's order (tier) by default.</summary>
    public BulkObservableCollection<TrackerItemViewModel> Trackers { get; } = [];

    /// <summary>In <see cref="PeerSort"/> order; fastest download first by default.</summary>
    public BulkObservableCollection<PeerItemViewModel> Peers { get; } = [];

    /// <summary>Keys of the tables, for remembering their sorts and column widths.</summary>
    public static class Tables
    {
        public const string Trackers = "trackers", Peers = "peers", Files = "files";
    }

    /// <summary>The tables' saved column widths (by table, then column), for the view to apply once settings are read.</summary>
    [ObservableProperty] private IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>? _columnWidths;

    /// <summary>The Trackers tab's column sort; set from the saved settings at start-up.</summary>
    [ObservableProperty] private TableSort _trackerSort = TrackerItemViewModel.DefaultSort;

    /// <summary>The Peers tab's column sort; set from the saved settings at start-up.</summary>
    [ObservableProperty] private TableSort _peerSort = PeerItemViewModel.DefaultSort;

    /// <summary>A click on a Trackers column header (its key): that column, or the other direction.</summary>
    [RelayCommand]
    private void SortTrackers(string column) =>
        TrackerSort = TableSorting.Toggle(TrackerSort, column, TrackerItemViewModel.StartsDescending);

    /// <summary>A click on a Peers column header (its key): that column, or the other direction.</summary>
    [RelayCommand]
    private void SortPeers(string column) => PeerSort = TableSorting.Toggle(PeerSort, column, PeerItemViewModel.StartsDescending);

    partial void OnTrackerSortChanged(TableSort value) => Resort(Trackers, TrackerItemViewModel.Order(value));
    partial void OnPeerSortChanged(TableSort value) => Resort(Peers, PeerItemViewModel.Order(value));

    private static void Resort<T>(BulkObservableCollection<T> rows, Comparison<T> order) where T : class
    {
        var target = rows.ToList();
        target.Sort(order);
        RowOrder.Apply(rows, target);
    }
    public FilesViewModel Files { get; }
    public TorrentOptionsViewModel Options { get; }

    /// <summary>True once the selected torrent's details have arrived.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private bool _hasDetails;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = NothingSelectedMessage;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string _trackersHeader = "Trackers";
    [ObservableProperty] private string _peersHeader = "Peers";
    [ObservableProperty] private string _filesHeader = "Files";

    private bool FilesTabShowing => SelectedTabIndex == FilesTabIndex;

    // Files are only fetched while their tab shows: a torrent can have thousands.
    partial void OnSelectedTabIndexChanged(int value)
    {
        if (_hash is not null)
            _feed.Watch(_hash, FilesTabShowing);
    }

    public bool HasMessage => !HasDetails && !IsLoading && Message.Length > 0;

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(HasMessage));

    public void Dispose()
    {
        _list.SelectionChanged -= OnSelectionChanged;
        _feed.DetailsUpdated -= OnDetailsUpdated;
        _feed.Watch(null);
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        var selected = _list.SelectedTorrents;
        var hash = selected.Count == 1 ? selected[0].Hash : null;
        if (hash == _hash && hash is not null) return;

        _hash = hash;
        Clear();

        if (hash is null)
        {
            Message = selected.Count == 0 ? NothingSelectedMessage : $"{selected.Count} torrents selected.";
            IsLoading = false;
        }
        else
        {
            Message = "";
            IsLoading = true;
        }

        _feed.Watch(hash, FilesTabShowing);
    }

    private void OnDetailsUpdated(object? sender, TorrentDetailsUpdate update)
    {
        if (update.Hash != _hash) return; // a late answer for the previous selection

        IsLoading = false;
        if (update.Details is not { } details)
        {
            Clear();
            Message = GoneMessage;
            return;
        }

        var now = _time.GetUtcNow();
        _name = details.Name;
        General.Apply(details);
        if (details.Options is { } options)
            Options.Load(update.Hash, options);
        Sync(Trackers, details.Trackers, t => t.Id, t => t.Id, id => new TrackerItemViewModel(id), (vm, t) => vm.Apply(t, now),
            TrackerItemViewModel.Order(TrackerSort));
        Sync(Peers, details.Peers, p => p.Key, PeerItemViewModel.KeyOf, key => new PeerItemViewModel(key), (vm, p) => vm.Apply(p),
            PeerItemViewModel.Order(PeerSort));
        if (details.Files is { } files)
        {
            Files.Apply(update.Hash, files);
            FilesHeader = $"Files ({files.Count})";
        }
        TrackersHeader = $"Trackers ({details.Trackers.Count})";
        PeersHeader = $"Peers ({details.Peers.Count})";
        Message = "";
        HasDetails = true;
    }

    [RelayCommand]
    private async Task AddTrackersAsync()
    {
        if (_hash is not { } hash) return;
        var existing = Trackers.Select(t => t.Address).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var text = await _dialogs.PromptAsync(new TextPrompt(
            "Add trackers",
            $"Add trackers to “{_name}”. Put one announce address on each line.",
            "Announce addresses",
            "Add",
            Multiline: true,
            Validate: t => ParseTrackerAddresses(t, existing).Error));
        if (text is null || _hash != hash) return;

        // Like Transmission's own clients: every new tracker gets a tier of its own, after the existing ones.
        var added = ParseTrackerAddresses(text, existing).Addresses;
        await SetTrackersAsync(hash, [..CurrentTiers(), ..added.Select(a => (IReadOnlyList<string>)[a])], "Couldn't add the trackers");
    }

    [RelayCommand]
    private async Task RemoveTrackerAsync(TrackerItemViewModel? tracker)
    {
        if (_hash is not { } hash || tracker is null) return;

        var confirmed = await _dialogs.ConfirmAsync(
            "Remove tracker",
            $"Remove {tracker.Address} from “{_name}”? To use it again you'll need its address.",
            "Remove");
        if (!confirmed || _hash != hash) return;

        await SetTrackersAsync(hash, CurrentTiers(without: tracker), "Couldn't remove the tracker");
    }

    private List<IReadOnlyList<string>> CurrentTiers(TrackerItemViewModel? without = null) => Trackers
        .Where(t => t != without)
        .GroupBy(t => t.Tier)
        .OrderBy(g => g.Key)
        // The server's order within a tier (by tracker id), whatever the table is sorted by on screen.
        .Select(g => (IReadOnlyList<string>)g.OrderBy(t => t.Id).Select(t => t.Address).ToList())
        .ToList();

    private async Task SetTrackersAsync(string hash, IReadOnlyList<IReadOnlyList<string>> tiers, string failureTitle)
    {
        try
        {
            await _actions.SetTrackersAsync(hash, tiers);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _dialogs.ShowMessageAsync(failureTitle, ex.Message);
        }
    }

    /// <summary>
    /// Reads one announce address per line (blank lines are skipped, repeats dropped). The error names the first
    /// line that isn't an http, https or udp address, or that the torrent already has.
    /// </summary>
    internal static (IReadOnlyList<string> Addresses, string? Error) ParseTrackerAddresses(string text, IReadOnlySet<string> existing)
    {
        var addresses = new List<string>();
        foreach (var line in text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            if (!Uri.TryCreate(line, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https" or "udp")
                || uri.Host.Length == 0)
                return ([], $"Not a tracker address: {line}. Use an http://, https:// or udp:// announce address.");
            if (existing.Contains(line))
                return ([], $"This torrent already has {line}.");
            if (!addresses.Contains(line, StringComparer.OrdinalIgnoreCase))
                addresses.Add(line);
        }
        return (addresses, null);
    }

    private void Clear()
    {
        HasDetails = false;
        Trackers.Clear();
        Peers.Clear();
        Files.Clear();
        Options.Clear();
        TrackersHeader = "Trackers";
        PeersHeader = "Peers";
        FilesHeader = "Files";
    }

    /// <summary>
    /// Updates rows in place, reusing row view models by key, and keeps them in the table's sort order: only rows that
    /// came, went or moved change (see <see cref="RowOrder"/>), so the list doesn't flicker or lose its scroll position.
    /// </summary>
    private static void Sync<TKey, TItem, TModel>(
        BulkObservableCollection<TItem> rows, IReadOnlyList<TModel> models,
        Func<TItem, TKey> rowKey, Func<TModel, TKey> modelKey,
        Func<TKey, TItem> create, Action<TItem, TModel> apply, Comparison<TItem> order)
        where TKey : notnull where TItem : class
    {
        var existing = rows.ToDictionary(rowKey);
        var target = new List<TItem>(models.Count);
        foreach (var model in models)
        {
            var key = modelKey(model);
            var row = existing.GetValueOrDefault(key) ?? create(key);
            apply(row, model);
            target.Add(row);
        }

        target.Sort(order);
        RowOrder.Apply(rows, target);
    }
}
