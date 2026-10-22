using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Formatting;

namespace Synchro.ViewModels.Details;

/// <summary>A priority choice in the Files tab.</summary>
public sealed record PriorityOption(FilePriority Value, string Label)
{
    public static readonly IReadOnlyList<PriorityOption> All =
    [
        new(FilePriority.High, "High"),
        new(FilePriority.Normal, "Normal"),
        new(FilePriority.Low, "Low"),
    ];

    public static PriorityOption High => All[0];
    public static PriorityOption Normal => All[1];
    public static PriorityOption Low => All[2];

    public static PriorityOption Of(FilePriority p) => All.First(o => o.Value == p);
}

/// <summary>
/// A file or folder in the Files tab. Folders aggregate their files: sizes add up, and the checkbox and priority
/// show a mixed state (null) when their files differ. Changes made by the user go to <see cref="FilesViewModel"/>,
/// which sends them to the server; values from the server arrive through <see cref="FilesViewModel.Apply"/>.
/// </summary>
public sealed partial class FileNodeViewModel : ObservableObject
{
    private readonly FilesViewModel _owner;
    private bool? _isWanted;
    private PriorityOption? _priority;

    internal FileNodeViewModel(FilesViewModel owner, string name, string path, int depth, int? fileIndex)
    {
        _owner = owner;
        Name = name;
        Path = path;
        Depth = depth;
        FileIndex = fileIndex;
    }

    public string Name { get; }

    /// <summary>Path inside the torrent; identifies the node across polls.</summary>
    public string Path { get; }

    public int Depth { get; }

    /// <summary>Indentation in pixels, so the view needs no converter.</summary>
    public double Indent => Depth * 20;

    /// <summary>The file's index for the protocol; null for folders.</summary>
    public int? FileIndex { get; }

    public bool IsFolder => FileIndex is null;

    /// <summary>Done and Progress only mean something once the torrent is on the server.</summary>
    public bool ShowsProgress => _owner.ShowsProgress;
    public List<FileNodeViewModel> Children { get; } = [];

    /// <summary>Indices of every file in this node (just its own for a file).</summary>
    public IReadOnlyList<int> FileIndices { get; private set; } = [];

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private string _sizeText = "";
    [ObservableProperty] private string _doneText = "";
    [ObservableProperty] private double _progress; // 0..100
    [ObservableProperty] private string _progressText = "";

    internal long Length { get; private set; }
    internal long BytesCompleted { get; private set; }

    /// <summary>Two-way bound to the checkbox. Setting it asks the server to download or skip these files.</summary>
    public bool? IsWanted
    {
        get => _isWanted;
        set
        {
            // A click on a mixed checkbox means "download all of it", as in Finder-style trees.
            var wanted = value ?? true;
            if (_isWanted is null && value is false) wanted = true;
            if (_isWanted == wanted) return;

            _owner.RequestWanted(this, wanted);
        }
    }

    /// <summary>Two-way bound to the priority box; null when a folder's files differ.</summary>
    public PriorityOption? Priority
    {
        get => _priority;
        set
        {
            if (value is null || value == _priority) return;
            _owner.RequestPriority(this, value.Value);
        }
    }

    /// <summary>"Mixed" when a folder's files have different priorities.</summary>
    public string PriorityText => _priority?.Label ?? "Mixed";

    /// <summary>For the priority menu: one command, the option as parameter.</summary>
    [RelayCommand]
    private void SetPriority(PriorityOption option) => Priority = option;

    public bool CanRename => _owner.CanRename;

    [RelayCommand]
    private void Rename() => _owner.RequestRename(this);

    partial void OnIsExpandedChanged(bool value) => _owner.RefreshRows();

    internal void SetFile(TorrentFile file)
    {
        Length = file.Length;
        BytesCompleted = file.BytesCompleted;
        SetState(file.Wanted, PriorityOption.Of(file.Priority));
        UpdateTexts();
    }

    /// <summary>Recomputes a folder from its children (children first).</summary>
    internal void Aggregate()
    {
        foreach (var child in Children.Where(c => c.IsFolder))
            child.Aggregate();

        Length = Children.Sum(c => c.Length);
        BytesCompleted = Children.Sum(c => c.BytesCompleted);
        FileIndices = Children.SelectMany(c => c.FileIndices).ToList();

        var wanted = Children.Select(c => c._isWanted).Distinct().ToList();
        var priorities = Children.Select(c => c._priority).Distinct().ToList();
        SetState(wanted.Count == 1 ? wanted[0] : null, priorities.Count == 1 ? priorities[0] : null);
        UpdateTexts();
    }

    internal void InitIndices() => FileIndices = FileIndex is { } i ? [i] : [];

    /// <summary>Optimistic update after a user change: files take the value, folders are recomputed by the owner.</summary>
    internal void SetLeaves(bool? wanted, PriorityOption? priority)
    {
        if (!IsFolder)
        {
            SetState(wanted ?? _isWanted, priority ?? _priority);
            return;
        }

        foreach (var child in Children)
            child.SetLeaves(wanted, priority);
    }

    private void SetState(bool? wanted, PriorityOption? priority)
    {
        if (_isWanted != wanted)
        {
            _isWanted = wanted;
            OnPropertyChanged(nameof(IsWanted));
        }

        if (_priority != priority)
        {
            _priority = priority;
            OnPropertyChanged(nameof(Priority));
            OnPropertyChanged(nameof(PriorityText));
        }
    }

    private void UpdateTexts()
    {
        SizeText = ByteFormatter.FormatSize(Length);
        DoneText = ByteFormatter.FormatSize(BytesCompleted);
        Progress = Length > 0 ? Math.Clamp((double)BytesCompleted / Length, 0, 1) * 100 : 100;
        ProgressText = $"{Progress.ToString("0.#", CultureInfo.CurrentCulture)}%";
    }
}
