using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Formatting;

namespace Synchro.ViewModels;

/// <summary>
/// Used and free space on the server's download disk, for the sidebar. Refreshed when the connection comes up and
/// then once a minute: disk space changes slowly, so there's no need to ask on every poll.
/// </summary>
public sealed partial class StorageViewModel : ObservableObject, IDisposable
{
    /// <summary>Below this share of free space the bar turns red.</summary>
    public const double LowSpaceFraction = 0.10;

    private readonly IStorageService _storage;
    private readonly IConnectionService _connection;
    private readonly TimeProvider _time;
    private CancellationTokenSource? _loop;

    public StorageViewModel(IStorageService storage, IConnectionService connection, TimeProvider time)
    {
        _storage = storage;
        _connection = connection;
        _time = time;
        _connection.StatusChanged += OnStatusChanged;
    }

    internal TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

    [ObservableProperty] private bool _hasStorage;
    [ObservableProperty] private bool _hasTotal;
    [ObservableProperty] private double _usedPercent; // 0..100
    [ObservableProperty] private bool _isLow;

    /// <summary>"1.9 TB used · 93 GB free", or "93 GB free" when the server doesn't report the disk size.</summary>
    [ObservableProperty] private string _summary = "";

    /// <summary>"of 2.0 TB".</summary>
    [ObservableProperty] private string _totalText = "";

    /// <summary>The folder measured (the server's default download folder), for the tooltip.</summary>
    [ObservableProperty] private string _folder = "";

    public void Dispose()
    {
        _connection.StatusChanged -= OnStatusChanged;
        _loop?.Cancel();
    }

    /// <summary>Asks the server once. A failed request keeps the last values; the next refresh tries again.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        StorageInfo? info;
        try
        {
            info = await _storage.GetDownloadStorageAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return;
        }

        if (!ct.IsCancellationRequested)
            Apply(info);
    }

    private void OnStatusChanged(object? sender, EventArgs e)
    {
        switch (_connection.Status.State)
        {
            case ConnectionState.Connected when _loop is null:
                _loop = new CancellationTokenSource();
                _ = RunAsync(_loop.Token);
                break;

            // Another server (or none): its disk is a different one.
            case ConnectionState.Connecting or ConnectionState.Disconnected:
                _loop?.Cancel();
                _loop = null;
                Apply(null);
                break;

            // A lost connection keeps showing the last values; the loop keeps trying.
        }
    }

    // No ConfigureAwait(false): updates must land on the UI thread, where the loop was started.
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

    private void Apply(StorageInfo? info)
    {
        HasStorage = info is not null;
        if (info is null)
        {
            Summary = TotalText = Folder = "";
            UsedPercent = 0;
            HasTotal = IsLow = false;
            return;
        }

        var free = ByteFormatter.FormatSize(info.FreeBytes);
        Folder = info.Path;
        HasTotal = info.HasTotal;
        if (info.HasTotal)
        {
            UsedPercent = (double)info.UsedBytes / info.TotalBytes * 100;
            IsLow = (double)info.FreeBytes / info.TotalBytes < LowSpaceFraction;
            Summary = $"{ByteFormatter.FormatSize(info.UsedBytes)} used · {free} free";
            TotalText = $"of {ByteFormatter.FormatSize(info.TotalBytes)} ({UsedPercent.ToString("0", CultureInfo.CurrentCulture)}% used)";
        }
        else
        {
            UsedPercent = 0;
            IsLow = false;
            Summary = $"{free} free";
            TotalText = "";
        }
    }
}
