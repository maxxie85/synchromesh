using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Services;

namespace Synchro.ViewModels;

/// <summary>
/// The turtle toggle in the toolbar: the server's alternative speed limits on or off. The server's own schedule can
/// switch them too, so the state is read on connect and every 30 seconds, and right after a toggle.
/// </summary>
public sealed partial class AltSpeedViewModel : ObservableObject, IDisposable
{
    private readonly ISpeedLimitService _speedLimits;
    private readonly IConnectionService _connection;
    private readonly IDialogService _dialogs;
    private readonly TimeProvider _time;
    private CancellationTokenSource? _loop;

    public AltSpeedViewModel(ISpeedLimitService speedLimits, IConnectionService connection, IDialogService dialogs, TimeProvider time)
    {
        _speedLimits = speedLimits;
        _connection = connection;
        _dialogs = dialogs;
        _time = time;
        _connection.StatusChanged += OnStatusChanged;
    }

    internal TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    private bool _isAvailable;

    [ObservableProperty] private bool _isEnabled;

    /// <summary>"Alternative speed limits: on (↓ 50 kB/s ↑ 20 kB/s)".</summary>
    [ObservableProperty] private string _toolTip = "Alternative speed limits";

    public void Dispose()
    {
        _connection.StatusChanged -= OnStatusChanged;
        _loop?.Cancel();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        AltSpeed? alt;
        try
        {
            alt = await _speedLimits.GetAltSpeedAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return; // keep showing the last known state; the next refresh tries again
        }

        Apply(alt);
    }

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private async Task ToggleAsync()
    {
        var wanted = !IsEnabled;
        IsEnabled = wanted; // the toggle button flips at once; the refresh below confirms or corrects it
        try
        {
            await _speedLimits.SetAltSpeedEnabledAsync(wanted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _dialogs.ShowMessageAsync("Couldn't change the speed limits", ex.Message);
        }

        await RefreshAsync();
    }

    private void OnStatusChanged(object? sender, EventArgs e)
    {
        switch (_connection.Status.State)
        {
            case ConnectionState.Connected when _loop is null:
                _loop = new CancellationTokenSource();
                _ = RunAsync(_loop.Token);
                break;

            case ConnectionState.Connecting or ConnectionState.Disconnected:
                _loop?.Cancel();
                _loop = null;
                Apply(null);
                break;
        }
    }

    // No ConfigureAwait(false): updates land on the UI thread, where the loop was started.
    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await RefreshAsync(ct);
                await Task.Delay(RefreshInterval, _time, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Apply(AltSpeed? alt)
    {
        IsAvailable = alt is not null;
        IsEnabled = alt?.Enabled ?? false;
        ToolTip = alt is null
            ? "Alternative speed limits"
            : $"Alternative speed limits: {(alt.Enabled ? "on" : "off")} " +
              $"(↓ {Rate(alt.DownloadKilobytesPerSecond)} ↑ {Rate(alt.UploadKilobytesPerSecond)})";
    }

    private static string Rate(int kilobytesPerSecond) =>
        $"{kilobytesPerSecond.ToString("N0", CultureInfo.CurrentCulture)} kB/s";
}
