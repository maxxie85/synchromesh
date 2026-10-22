using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Synchro.AppServices.Torrents;
using Synchro.Core.Metainfo;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Details;
using Synchro.ViewModels.Formatting;
using Synchro.ViewModels.Services;

namespace Synchro.ViewModels;

/// <summary>Where the Add dialog is: entering the source, waiting for a link's file list, or choosing files.</summary>
public enum AddStage
{
    Input,
    FetchingFiles,
    ChoosingFiles,
}

/// <summary>
/// The Add dialog. A .torrent file shows its files right away. With "choose files first" on, a link is added first and
/// the dialog waits for the server to have the file list, lets the user choose, applies the choices and starts it.
/// A torrent URL is added paused (the server fetches the .torrent itself). A magnet link has to run to get its file
/// list from peers (a stopped torrent contacts nobody), so it is added running and stopped the moment the list
/// arrives. Cancelling (or closing the window) after the add removes the torrent again.
/// </summary>
public sealed partial class AddTorrentViewModel : ObservableObject
{
    private readonly IAddTorrentService _service;
    private readonly IFilePicker _picker;
    private string _defaultDirectory = "";
    private bool _initializing;
    private CancellationTokenSource? _freeSpaceCts;
    private CancellationTokenSource? _fetchCts;

    // A torrent added for choosing files; removed again if the user cancels.
    private AddTorrentResult? _pending;

    // A magnet link added running so it can fetch its file list; it is stopped as soon as the list is there.
    private bool _runningForFileList;

    /// <summary>
    /// How often to ask the server whether a link's file list has arrived. Short, because a magnet link starts
    /// downloading the moment its list arrives, until the dialog stops it. Tests shorten it further.
    /// </summary>
    internal TimeSpan FileListPollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    public AddTorrentViewModel(IAddTorrentService service, IFilePicker picker)
    {
        _service = service;
        _picker = picker;
    }

    /// <summary>The argument is the result, or null when cancelled.</summary>
    public event EventHandler<AddTorrentResult?>? CloseRequested;

    /// <summary>Set when the torrent comes from a .torrent file (dropped, picked, or opened from Finder).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile), nameof(IsLinkMode), nameof(FileSummary), nameof(ShowsChooseFilesFirst), nameof(AddButtonText))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private AddTorrentSource.TorrentFile? _selectedFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LinkError), nameof(HasLinkError))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _linkText = "";

    [ObservableProperty] private string _downloadDirectory = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddButtonText))]
    private bool _startPaused;

    /// <summary>Pieces in order, for playing a video while it downloads. Sent with the add.</summary>
    [ObservableProperty] private bool _sequentialDownload;

    /// <summary>For links: add paused and show the file list before downloading.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddButtonText))]
    private bool _chooseFilesFirst = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInput), nameof(IsFetchingFiles), nameof(AddButtonText), nameof(CancelButtonText), nameof(ShowsChooseFilesFirst))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand), nameof(DownloadEverythingCommand))]
    private AddStage _stage;

    /// <summary>"Getting the file list from peers… 40%".</summary>
    [ObservableProperty] private string _fetchText = "";

    /// <summary>Name of the torrent being added, once the server knows it.</summary>
    [ObservableProperty] private string _torrentName = "";

    public bool IsInput => Stage == AddStage.Input;
    public bool IsFetchingFiles => Stage == AddStage.FetchingFiles;

    public string AddButtonText => Stage switch
    {
        AddStage.Input when IsLinkMode && ChooseFilesFirst => "Next",
        AddStage.ChoosingFiles when !StartPaused => "Start download",
        _ => "Add",
    };

    /// <summary>After the torrent went to the server, cancelling takes it back; the button says so.</summary>
    public string CancelButtonText => Stage == AddStage.Input ? "Cancel" : "Cancel adding";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFreeSpace))]
    private string? _freeSpaceText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand), nameof(ChooseFileCommand), nameof(DownloadEverythingCommand))]
    private bool _isAdding;

    /// <summary>
    /// The file tree when the .torrent lists more than one file; null for a link, a single-file torrent, or a
    /// torrent this app can't read (it is then added with all files, as before).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFileChoice))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private FilesViewModel? _files;

    public bool HasFileChoice => Files is not null;

    public bool HasFile => SelectedFile is not null;
    public bool IsLinkMode => !HasFile;

    /// <summary>The "choose files first" option only applies to links in the first step.</summary>
    public bool ShowsChooseFilesFirst => IsLinkMode && IsInput;
    public string FileSummary => SelectedFile?.FileName ?? "";
    public bool HasFreeSpace => !string.IsNullOrEmpty(FreeSpaceText);
    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public string? LinkError =>
        string.IsNullOrWhiteSpace(LinkText) || AddTorrentSource.TryParseLink(LinkText, out _)
            ? null
            : "Enter a magnet link, or a link to a .torrent file.";

    public bool HasLinkError => LinkError is not null;

    /// <summary>Pre-fills the form, then loads the server's default folder. Safe to run while the dialog is already showing.</summary>
    public async Task InitializeAsync(AddTorrentSource? initial, CancellationToken ct = default)
    {
        // The source is applied before the first await so the dialog shows it immediately.
        switch (initial)
        {
            case AddTorrentSource.TorrentFile file: SelectedFile = file; break;
            case AddTorrentSource.Magnet magnet: LinkText = magnet.Link; break;
            case AddTorrentSource.Url url: LinkText = url.Address.ToString(); break;
        }

        string directory;
        try
        {
            directory = await _service.GetDefaultDownloadDirectoryAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return; // Leave the folder empty: the server then picks its own default.
        }

        _defaultDirectory = directory;
        if (string.IsNullOrWhiteSpace(DownloadDirectory))
        {
            _initializing = true;
            DownloadDirectory = directory;
            _initializing = false;
        }

        await RefreshFreeSpaceAsync(DownloadDirectory, ct);
    }

    /// <summary>Looks up free space for a folder on the server. A failure shows a short note rather than an error.</summary>
    public async Task RefreshFreeSpaceAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            FreeSpaceText = null;
            return;
        }

        try
        {
            var bytes = await _service.GetFreeSpaceAsync(path.Trim(), ct);
            if (!ct.IsCancellationRequested)
                FreeSpaceText = $"{ByteFormatter.FormatSize(bytes)} free";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            FreeSpaceText = "Couldn't check the free space for this folder.";
        }
    }

    // Typing a folder re-checks the free space, but only once the user pauses.
    partial void OnDownloadDirectoryChanged(string value)
    {
        if (_initializing) return;

        _freeSpaceCts?.Cancel();
        var cts = _freeSpaceCts = new CancellationTokenSource();
        _ = RefreshFreeSpaceAfterPauseAsync(value, cts.Token);
    }

    private async Task RefreshFreeSpaceAfterPauseAsync(string path, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), ct);
            await RefreshFreeSpaceAsync(path, ct);
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke superseded this lookup.
        }
    }

    private AddTorrentSource? ResolveSource()
    {
        if (SelectedFile is not null) return SelectedFile;
        return AddTorrentSource.TryParseLink(LinkText, out var source) ? source : null;
    }

    // With a file tree, at least one file must be ticked: a torrent with nothing to download would just sit there.
    private bool CanAdd() => !IsAdding && Files is not { WantedCount: 0 } && Stage switch
    {
        AddStage.Input => ResolveSource() is not null,
        AddStage.ChoosingFiles => true,
        _ => false,
    };

    partial void OnSelectedFileChanged(AddTorrentSource.TorrentFile? value)
        => SetFiles(value is not null && TorrentMetainfo.TryParse(value.Content, out var metainfo)
            ? CreateFileTree(metainfo!.Name, metainfo.Files.Where(f => !f.IsPadding)
                .Select(f => new TorrentFile(f.Index, f.Path, f.Length, 0, true, FilePriority.Normal)).ToList())
            : null);

    partial void OnChooseFilesFirstChanged(bool value) => AddCommand.NotifyCanExecuteChanged();

    private void SetFiles(FilesViewModel? files)
    {
        if (Files is not null)
            Files.PropertyChanged -= OnFilesChanged;

        Files = files;

        if (Files is not null)
            Files.PropertyChanged += OnFilesChanged;
    }

    private void OnFilesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilesViewModel.WantedCount))
            AddCommand.NotifyCanExecuteChanged();
    }

    // Padding files keep their index (it must match the server's) but are not shown; the caller leaves them out.
    // One file leaves nothing to choose.
    private static FilesViewModel? CreateFileTree(string name, IReadOnlyList<TorrentFile> files)
    {
        if (files.Count < 2) return null;

        var tree = FilesViewModel.ForChoosing();
        tree.Apply(name, files);
        return tree;
    }

    // Server-side file lists name BEP 47 padding files ".pad/<n>".
    private static bool IsPaddingPath(string path) => path.Split('/').Contains(".pad");
    private bool CanChooseFile() => !IsAdding;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync(CancellationToken ct)
    {
        if (Stage == AddStage.ChoosingFiles)
        {
            var choices = Files?.GetChoices();
            await FinishAsync(choices is { IsDefault: false } ? choices : null);
            return;
        }

        var source = ResolveSource();
        if (source is null) return;

        var chooseFirst = source is not AddTorrentSource.TorrentFile && ChooseFilesFirst;
        var runForFileList = chooseFirst && source is AddTorrentSource.Magnet;

        IsAdding = true;
        StatusMessage = null;
        try
        {
            // Only override the server's folder when the user actually chose a different one.
            var directory = DownloadDirectory.Trim();
            // Choices only apply to the .torrent they were made for, not to a link typed after removing it.
            var choices = source is AddTorrentSource.TorrentFile ? Files?.GetChoices() : null;
            var request = new AddTorrentRequest(
                source,
                directory.Length == 0 || directory == _defaultDirectory ? null : directory,
                // Paused while choosing, except a magnet link: stopped, it would never get its file list.
                runForFileList ? false : StartPaused || chooseFirst,
                choices is { IsDefault: false } ? choices : null,
                SequentialDownload);

            var result = await _service.AddAsync(request, ct);

            // A torrent the server already had is left exactly as it is.
            if (!chooseFirst || result.Outcome == AddTorrentOutcome.Duplicate)
            {
                CloseRequested?.Invoke(this, result);
                return;
            }

            _pending = result;
            _runningForFileList = runForFileList;
            TorrentName = result.Name;
            FetchText = "Getting the file list from peers…";
            Stage = AddStage.FetchingFiles;
            _fetchCts = new CancellationTokenSource();
            _ = WaitForFileListAsync(result.HashString, _fetchCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't add the torrent: {ex.Message}";
        }
        finally
        {
            IsAdding = false;
        }
    }

    /// <summary>
    /// Polls until the server has the link's file list. Peers may be slow to send it (or never do), so this keeps
    /// waiting quietly; the user can download everything instead, or cancel.
    /// </summary>
    private async Task WaitForFileListAsync(string hash, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TorrentFileList? list;
                try
                {
                    list = await _service.GetFileListAsync(hash, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    FetchText = $"Couldn't ask the server for the file list ({ex.Message}). Trying again…";
                    await Task.Delay(FileListPollInterval, ct);
                    continue;
                }

                if (ct.IsCancellationRequested) return;

                if (list is null)
                {
                    _pending = null; // nothing left to take back
                    FetchText = "";
                    StatusMessage = "The torrent was removed from the server before its file list arrived.";
                    return;
                }

                if (!string.IsNullOrEmpty(list.Name))
                    TorrentName = list.Name;

                if (list.HasMetadata)
                {
                    var tree = CreateFileTree(list.Name, list.Files.Where(f => !IsPaddingPath(f.Path)).ToList());
                    if (tree is null)
                    {
                        await FinishAsync(null); // a single file: nothing to choose
                        return;
                    }

                    // Stop the magnet before anything else: from here on it would download everything.
                    if (_runningForFileList)
                    {
                        try
                        {
                            await _service.HoldForChoiceAsync(hash, ct);
                            _runningForFileList = false;
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            StatusMessage = $"Couldn't pause the download while you choose: {ex.Message}";
                        }
                    }

                    SetFiles(tree);
                    Stage = AddStage.ChoosingFiles;
                    return;
                }

                var percent = Math.Clamp(list.MetadataPercentComplete, 0, 1) * 100;
                FetchText = percent > 0
                    ? $"Getting the file list from peers… {percent:0}%"
                    : "Getting the file list from peers… This can take a while when few peers are online.";
                await Task.Delay(FileListPollInterval, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled, or the user chose to download everything.
        }
    }

    private bool CanDownloadEverything() => Stage == AddStage.FetchingFiles && !IsAdding && _pending is not null;

    /// <summary>Skips choosing: downloads all files (or adds them paused, if that was asked for).</summary>
    [RelayCommand(CanExecute = nameof(CanDownloadEverything))]
    private Task DownloadEverythingAsync()
    {
        _fetchCts?.Cancel();
        return FinishAsync(null);
    }

    private async Task FinishAsync(FileChoices? choices)
    {
        if (_pending is not { } pending) return;

        IsAdding = true;
        StatusMessage = null;
        try
        {
            await _service.FinishAddAsync(pending.HashString, choices, start: !StartPaused);
            _pending = null;
            CloseRequested?.Invoke(this, pending);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = $"Couldn't apply your choices: {ex.Message}";
        }
        finally
        {
            IsAdding = false;
        }
    }

    /// <summary>
    /// Called when the dialog closes without finishing (window closed): takes back a torrent that was added paused
    /// for choosing. Best effort; a failure leaves it paused on the server, where the user can remove it.
    /// </summary>
    public async Task AbandonAsync()
    {
        _fetchCts?.Cancel();
        if (_pending is not { } pending) return;

        _pending = null;
        try
        {
            await _service.UndoAddAsync(pending.HashString);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nothing to show it in any more: the dialog is gone.
        }
    }

    [RelayCommand(CanExecute = nameof(CanChooseFile))]
    private async Task ChooseFileAsync(CancellationToken ct)
    {
        try
        {
            var picked = await _picker.PickTorrentFileAsync(ct);
            if (picked is null) return;

            SelectedFile = picked;
            StatusMessage = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = $"Couldn't read that file: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RemoveFile() => SelectedFile = null;

    [RelayCommand]
    private async Task CancelAsync()
    {
        await AbandonAsync();
        CloseRequested?.Invoke(this, null);
    }
}
