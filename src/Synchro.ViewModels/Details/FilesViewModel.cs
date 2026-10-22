using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Services;

namespace Synchro.ViewModels.Details;

/// <summary>
/// A torrent's files as a tree, shown as a flat list of the visible rows (so columns line up at any depth).
/// Used by the Files tab, where changes go to the server and each poll updates the tree in place, and by the
/// Add dialog (<see cref="ForChoosing"/>), where changes stay local until the torrent is added.
/// </summary>
public sealed partial class FilesViewModel : ObservableObject
{
    private readonly ITorrentActions? _actions;
    private readonly IDialogService? _dialogs;

    private readonly List<FileNodeViewModel> _roots = [];
    private readonly Dictionary<int, FileNodeViewModel> _byIndex = [];
    private string? _hash;
    private IReadOnlyList<string> _paths = [];
    private string _rootFolder = ""; // "Name/" when every file is in the torrent's folder (not shown as a row)

    /// <summary>For a torrent on the server: changes are sent right away.</summary>
    public FilesViewModel(ITorrentActions actions, IDialogService dialogs) : this(actions, dialogs, showsProgress: true)
    {
    }

    private FilesViewModel(ITorrentActions? actions, IDialogService? dialogs, bool showsProgress)
    {
        _actions = actions;
        _dialogs = dialogs;
        ShowsProgress = showsProgress;
    }

    /// <summary>For a torrent not added yet: changes stay here; read them with <see cref="GetChoices"/>.</summary>
    public static FilesViewModel ForChoosing() => new(null, null, showsProgress: false);

    /// <summary>False before adding: nothing is downloaded yet, so Done and Progress are hidden.</summary>
    public bool ShowsProgress { get; }

    public IReadOnlyList<PriorityOption> PriorityOptions => PriorityOption.All;

    /// <summary>Files on the server can be renamed; files chosen before adding can't.</summary>
    public bool CanRename => _actions is not null;

    /// <summary>"5 of 7 files, 8.3 GB": what will be downloaded.</summary>
    [ObservableProperty] private string _summary = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NothingWanted))]
    private int _wantedCount;

    /// <summary>Every file is unticked: the Add dialog says so and won't add.</summary>
    public bool NothingWanted => WantedCount == 0 && _byIndex.Count > 0;

    /// <summary>The visible rows: top-level nodes and the children of expanded folders, in tree order.</summary>
    public ObservableCollection<FileNodeViewModel> Rows { get; } = [];

    [ObservableProperty] private bool _hasFiles;
    [ObservableProperty] private int _fileCount;

    public void Clear()
    {
        _hash = null;
        _paths = [];
        _roots.Clear();
        _byIndex.Clear();
        Rows.Clear();
        HasFiles = false;
        FileCount = 0;
        WantedCount = 0;
        Summary = "";
    }

    public void Apply(string hash, IReadOnlyList<TorrentFile> files)
    {
        var paths = files.Select(f => f.Path).ToList();
        if (hash != _hash || !paths.SequenceEqual(_paths))
        {
            Build(files);
            _hash = hash;
            _paths = paths;
        }

        foreach (var file in files)
        {
            if (_byIndex.TryGetValue(file.Index, out var node))
                node.SetFile(file);
        }

        foreach (var root in _roots.Where(r => r.IsFolder))
            root.Aggregate();

        HasFiles = files.Count > 0;
        FileCount = files.Count;
        UpdateSummary();
    }

    /// <summary>The differences from the defaults (all files wanted, normal priority), by file index.</summary>
    public FileChoices GetChoices()
    {
        var files = _byIndex.Values.OrderBy(f => f.FileIndex).ToList();
        return new FileChoices(
            files.Where(f => f.IsWanted == false).Select(f => f.FileIndex!.Value).ToList(),
            files.Where(f => f.Priority?.Value == FilePriority.High).Select(f => f.FileIndex!.Value).ToList(),
            files.Where(f => f.Priority?.Value == FilePriority.Low).Select(f => f.FileIndex!.Value).ToList());
    }

    internal void RefreshRows()
    {
        var visible = new List<FileNodeViewModel>();
        void Add(FileNodeViewModel node)
        {
            visible.Add(node);
            if (node.IsFolder && node.IsExpanded)
                node.Children.ForEach(Add);
        }
        _roots.ForEach(Add);

        if (visible.SequenceEqual(Rows)) return;

        Rows.Clear();
        foreach (var node in visible)
            Rows.Add(node);
    }

    internal async void RequestWanted(FileNodeViewModel node, bool wanted)
    {
        if (_hash is not { } hash) return;

        node.SetLeaves(wanted, null);
        ReaggregateAll();
        if (_actions is not { } actions) return;

        await RunAsync("Couldn't change which files to download",
            () => actions.SetFilesWantedAsync(hash, node.FileIndices, wanted));
    }

    internal async void RequestPriority(FileNodeViewModel node, FilePriority priority)
    {
        if (_hash is not { } hash) return;

        node.SetLeaves(null, PriorityOption.Of(priority));
        ReaggregateAll();
        if (_actions is not { } actions) return;

        await RunAsync("Couldn't change the priority", () => actions.SetFilePriorityAsync(hash, node.FileIndices, priority));
    }

    /// <summary>The node's path as the server knows it: folders here leave out the torrent's own folder.</summary>
    internal string ServerPath(FileNodeViewModel node) => node.IsFolder ? _rootFolder + node.Path : node.Path;

    internal async void RequestRename(FileNodeViewModel node)
    {
        if (_hash is not { } hash || _actions is not { } actions || _dialogs is not { } dialogs) return;

        var path = ServerPath(node);
        var name = await dialogs.PromptAsync(NameRules.RenamePrompt(node.IsFolder ? "folder" : "file", node.Name));
        if (name is null || _hash != hash) return;

        await RunAsync("Couldn't rename it", () => actions.RenamePathAsync(hash, path, name));
    }

    // The next poll (requested by the action) brings the server's state back, so a failure only needs explaining.
    private async Task RunAsync(string failureTitle, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (_dialogs is not null)
                await _dialogs.ShowMessageAsync(failureTitle, ex.Message);
        }
    }

    private void ReaggregateAll()
    {
        foreach (var root in _roots.Where(r => r.IsFolder))
            root.Aggregate();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var wanted = _byIndex.Values.Where(f => f.IsWanted == true).ToList();
        WantedCount = wanted.Count;
        Summary = $"{wanted.Count} of {_byIndex.Count} files, {Formatting.ByteFormatter.FormatSize(wanted.Sum(f => f.Length))}";
    }

    private void Build(IReadOnlyList<TorrentFile> files)
    {
        _roots.Clear();
        _byIndex.Clear();

        // Multi-file torrents put everything in one folder named after the torrent; the tab already is that folder.
        var parts = files.Select(f => f.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)).ToList();
        var skip = parts.Count > 0 && parts.All(p => p.Length > 1 && p[0] == parts[0][0]) ? 1 : 0;
        _rootFolder = skip == 1 ? parts[0][0] + "/" : "";

        var folders = new Dictionary<string, FileNodeViewModel>();
        for (var i = 0; i < files.Count; i++)
        {
            var segments = parts[i][skip..];
            List<FileNodeViewModel> siblings = _roots;
            var path = "";
            for (var depth = 0; depth < segments.Length - 1; depth++)
            {
                path = path.Length == 0 ? segments[depth] : $"{path}/{segments[depth]}";
                if (!folders.TryGetValue(path, out var folder))
                {
                    folder = new FileNodeViewModel(this, segments[depth], path, depth, null);
                    folders[path] = folder;
                    siblings.Add(folder);
                }
                siblings = folder.Children;
            }

            var name = segments.Length > 0 ? segments[^1] : files[i].Path;
            var leaf = new FileNodeViewModel(this, name, files[i].Path, segments.Length - 1, files[i].Index);
            leaf.InitIndices();
            siblings.Add(leaf);
            _byIndex[files[i].Index] = leaf;
        }

        Sort(_roots);

        // One folder at the top: open it, there is nothing else to see.
        if (_roots is [{ IsFolder: true } only])
            only.IsExpanded = true;

        RefreshRows();
    }

    // Folders first, then names in natural order (2 before 10), like Finder.
    private static void Sort(List<FileNodeViewModel> nodes)
    {
        nodes.Sort((a, b) => a.IsFolder != b.IsFolder
            ? (a.IsFolder ? -1 : 1)
            : Formatting.NaturalStringComparer.Instance.Compare(a.Name, b.Name));
        foreach (var n in nodes.Where(n => n.IsFolder))
            Sort(n.Children);
    }
}
