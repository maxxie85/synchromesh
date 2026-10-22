using CommunityToolkit.Mvvm.ComponentModel;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Formatting;

namespace Synchro.ViewModels;

public sealed partial class StatusBarViewModel : ObservableObject, IDisposable
{
    private readonly IConnectionService _connection;
    private readonly ITorrentFeed _feed;

    [ObservableProperty] private string _text = "Not connected";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private bool _hasError;

    /// <summary>Connected, but to a server whose RPC version is newer than the app was tested with.</summary>
    [ObservableProperty] private bool _hasWarning;
    [ObservableProperty] private string _downloadSpeedText = "";
    [ObservableProperty] private string _uploadSpeedText = "";
    [ObservableProperty] private string _countText = "";

    public StatusBarViewModel(IConnectionService connection, ITorrentFeed feed)
    {
        _connection = connection;
        _feed = feed;
        _connection.StatusChanged += OnStatusChanged;
        _feed.StatsUpdated += OnStatsUpdated;
        Apply(connection.Status);
    }

    public void Dispose()
    {
        _connection.StatusChanged -= OnStatusChanged;
        _feed.StatsUpdated -= OnStatsUpdated;
    }

    private void OnStatusChanged(object? sender, EventArgs e) => Apply(_connection.Status);

    private void OnStatsUpdated(object? sender, SessionStats stats)
    {
        DownloadSpeedText = $"↓ {ByteFormatter.FormatRate(stats.DownloadSpeed)}";
        UploadSpeedText = $"↑ {ByteFormatter.FormatRate(stats.UploadSpeed)}";
        CountText = stats.TorrentCount == 1 ? "1 torrent" : $"{stats.TorrentCount} torrents";
    }

    private void Apply(ConnectionStatus status)
    {
        var name = _connection.ActiveProfile?.Name;

        IsConnected = status.State == ConnectionState.Connected;
        IsConnecting = status.State == ConnectionState.Connecting;
        HasError = status.State == ConnectionState.Failed;
        var warning = IsConnected ? status.Capabilities?.Warning : null;
        HasWarning = warning is not null;

        Text = status.State switch
        {
            ConnectionState.Connected => $"Connected to {name} (Transmission {status.ServerVersion})" +
                (warning is null ? "" : $". {warning}"),
            ConnectionState.Connecting => $"Connecting to {name}…",
            ConnectionState.Failed => status.Message ?? "Connection failed",
            _ => "Not connected",
        };

        // Stale numbers would mislead while there is no live connection.
        if (!IsConnected)
        {
            DownloadSpeedText = "";
            UploadSpeedText = "";
            CountText = "";
        }
    }
}
