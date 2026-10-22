using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Synchro.Core.Torrents;

namespace Synchro.ViewModels;

/// <summary>
/// The Speed limits dialog. It opens at once and reads the server's limits itself (<see cref="LoadAsync"/>), so the
/// click never waits for the network; Save stays off until they're there.
/// </summary>
public sealed partial class SpeedLimitsViewModel : ObservableValidator
{
    private readonly Func<CancellationToken, Task<SpeedLimits>> _load;

    public SpeedLimitsViewModel(Func<CancellationToken, Task<SpeedLimits>> load)
    {
        _load = load;
        ErrorsChanged += (_, _) => SaveCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty] private bool _downloadLimited;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 10_000_000, ErrorMessage = "Enter a speed between 1 and 10,000,000 kB/s.")]
    private int _downloadLimit = 1;

    [ObservableProperty] private bool _uploadLimited;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(1, 10_000_000, ErrorMessage = "Enter a speed between 1 and 10,000,000 kB/s.")]
    private int _uploadLimit = 1;

    [ObservableProperty] private bool _alternativeSpeedsEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isLoading = true;

    /// <summary>Why the limits couldn't be read; the dialog then only offers Cancel.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(HasLoadError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string? _loadError;

    public bool HasLoadError => LoadError is not null;

    /// <summary>The server's limits are shown and can be edited.</summary>
    public bool IsReady => !IsLoading && LoadError is null;

    /// <summary>The argument is the new limits, or null when cancelled.</summary>
    public event EventHandler<SpeedLimits?>? CloseRequested;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var current = await _load(ct);
            DownloadLimited = current.DownloadLimited;
            DownloadLimit = Math.Max(current.DownloadKilobytesPerSecond, 1);
            UploadLimited = current.UploadLimited;
            UploadLimit = Math.Max(current.UploadKilobytesPerSecond, 1);
            AlternativeSpeedsEnabled = current.AlternativeSpeedsEnabled;
            ClearErrors(); // the server's values are taken as given
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LoadError = $"Couldn't read the server's speed limits: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanSave() => IsReady && !HasErrors;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        ValidateAllProperties();
        if (HasErrors) return;

        CloseRequested?.Invoke(this, new SpeedLimits(
            DownloadLimited, DownloadLimit, UploadLimited, UploadLimit, AlternativeSpeedsEnabled));
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, null);
}
