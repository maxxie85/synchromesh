using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Services;

namespace Synchro.ViewModels.Details;

/// <summary>A labelled option for a dropdown. Non-generic base, so XAML templates can bind to the label.</summary>
public abstract record LabeledChoice(string Label);

public sealed record Choice<T>(T Value, string Label) : LabeledChoice(Label);

/// <summary>
/// The Options tab: one torrent's speed limits, priority, seeding limits and peer limit. Edits stay in the form until
/// Apply (or Revert): the details poll every 2 seconds, and must not overwrite what the user is typing.
/// </summary>
public sealed partial class TorrentOptionsViewModel : ObservableValidator
{
    private readonly ITorrentActions _actions;
    private readonly IDialogService _dialogs;
    private string? _hash;
    private TorrentOptions? _loaded;
    private bool _loading;

    public TorrentOptionsViewModel(ITorrentActions actions, IDialogService dialogs)
    {
        _actions = actions;
        _dialogs = dialogs;
        ErrorsChanged += (_, _) => ApplyCommand.NotifyCanExecuteChanged();
    }

    public IReadOnlyList<Choice<BandwidthPriority>> Priorities { get; } =
    [
        new(BandwidthPriority.High, "High"),
        new(BandwidthPriority.Normal, "Normal"),
        new(BandwidthPriority.Low, "Low"),
    ];

    public IReadOnlyList<Choice<SeedLimitMode>> RatioModes { get; } =
    [
        new(SeedLimitMode.ServerDefault, "Use the server's setting"),
        new(SeedLimitMode.Custom, "Stop at ratio"),
        new(SeedLimitMode.Unlimited, "Keep seeding"),
    ];

    public IReadOnlyList<Choice<SeedLimitMode>> IdleModes { get; } =
    [
        new(SeedLimitMode.ServerDefault, "Use the server's setting"),
        new(SeedLimitMode.Custom, "Stop when idle for"),
        new(SeedLimitMode.Unlimited, "Keep seeding"),
    ];

    [ObservableProperty] private bool _hasOptions;
    [ObservableProperty] private bool _downloadLimited;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(0, 10_000_000, ErrorMessage = "Enter a speed between 0 and 10,000,000 kB/s.")]
    private int _downloadLimit;

    [ObservableProperty] private bool _uploadLimited;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(0, 10_000_000, ErrorMessage = "Enter a speed between 0 and 10,000,000 kB/s.")]
    private int _uploadLimit;

    [ObservableProperty] private bool _honorsSessionLimits = true;
    [ObservableProperty] private Choice<BandwidthPriority>? _priority;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomRatio))]
    private Choice<SeedLimitMode>? _ratioMode;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(0.0, 1000.0, ErrorMessage = "Enter a ratio between 0 and 1000, for example 2.0.")]
    private double _ratioLimit = 2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomIdle))]
    private Choice<SeedLimitMode>? _idleMode;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 100_000, ErrorMessage = "Enter a number of minutes, at least 1.")]
    private int _idleLimitMinutes = 30;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 10_000, ErrorMessage = "Enter a number of peers between 1 and 10,000.")]
    private int _peerLimit = 50;

    [ObservableProperty] private bool _sequentialDownload;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(RevertCommand))]
    private bool _isDirty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isSaving;

    public bool HasCustomRatio => RatioMode?.Value == SeedLimitMode.Custom;
    public bool HasCustomIdle => IdleMode?.Value == SeedLimitMode.Custom;

    /// <summary>
    /// Shows the server's options for a torrent. Ignored while the user has unapplied edits for the same torrent;
    /// another torrent always replaces the form.
    /// </summary>
    public void Load(string hash, TorrentOptions options)
    {
        if (hash == _hash && (IsDirty || options == _loaded)) return;

        _hash = hash;
        _loaded = options;
        _loading = true;
        DownloadLimited = options.DownloadLimited;
        DownloadLimit = options.DownloadLimitKilobytes;
        UploadLimited = options.UploadLimited;
        UploadLimit = options.UploadLimitKilobytes;
        HonorsSessionLimits = options.HonorsSessionLimits;
        Priority = Priorities.First(p => p.Value == options.Priority);
        RatioMode = RatioModes.First(m => m.Value == options.RatioMode);
        RatioLimit = options.RatioLimit;
        IdleMode = IdleModes.First(m => m.Value == options.IdleMode);
        IdleLimitMinutes = Math.Max(options.IdleLimitMinutes, 1);
        PeerLimit = Math.Max(options.PeerLimit, 1);
        SequentialDownload = options.SequentialDownload;
        _loading = false;

        ClearErrors();
        IsDirty = false;
        HasOptions = true;
    }

    public void Clear()
    {
        _hash = null;
        _loaded = null;
        HasOptions = false;
        IsDirty = false;
    }

    private bool CanApply() => IsDirty && !IsSaving && !HasErrors;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_hash is not { } hash) return;
        ValidateAllProperties();
        if (HasErrors) return;

        var options = Build();
        IsSaving = true;
        try
        {
            await _actions.SetOptionsAsync(hash, options);
            _loaded = options;
            IsDirty = false; // the next poll confirms the server's values and loads them
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _dialogs.ShowMessageAsync("Couldn't change the torrent's options", ex.Message);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Revert()
    {
        if (_hash is { } hash && _loaded is { } loaded)
        {
            IsDirty = false;
            _hash = null; // force the reload
            Load(hash, loaded);
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_loading || _loaded is null) return;
        if (e.PropertyName is nameof(IsDirty) or nameof(IsSaving) or nameof(HasOptions) or nameof(HasErrors)
            or nameof(HasCustomRatio) or nameof(HasCustomIdle)) return;

        IsDirty = Build() != _loaded;
    }

    private TorrentOptions Build() => new(
        DownloadLimited, DownloadLimit, UploadLimited, UploadLimit, HonorsSessionLimits,
        Priority?.Value ?? BandwidthPriority.Normal,
        RatioMode?.Value ?? SeedLimitMode.ServerDefault, RatioLimit,
        IdleMode?.Value ?? SeedLimitMode.ServerDefault, IdleLimitMinutes,
        PeerLimit, SequentialDownload);
}
