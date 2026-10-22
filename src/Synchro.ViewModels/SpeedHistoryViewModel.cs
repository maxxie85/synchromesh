using CommunityToolkit.Mvvm.ComponentModel;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Formatting;

namespace Synchro.ViewModels;

/// <summary>The server's total download and upload rate at one poll, in bytes per second.</summary>
public readonly record struct SpeedSample(DateTimeOffset Time, long Download, long Upload);

/// <summary>
/// The last few minutes of total download and upload speed (from `session_stats`, one sample per poll) for the
/// sidebar graph. Cleared whenever the connection is not up: another server, or a gap, is a different line.
/// </summary>
public sealed partial class SpeedHistoryViewModel : ObservableObject, IDisposable
{
    /// <summary>The smallest top of the scale, so an idle server draws a flat line instead of magnified noise.</summary>
    public const long MinimumScale = 100_000;

    private readonly ITorrentFeed _feed;
    private readonly IConnectionService _connection;
    private readonly TimeProvider _time;
    private readonly List<SpeedSample> _history = [];

    public SpeedHistoryViewModel(ITorrentFeed feed, IConnectionService connection, TimeProvider time)
    {
        _feed = feed;
        _connection = connection;
        _time = time;
        _feed.StatsUpdated += OnStatsUpdated;
        _connection.StatusChanged += OnStatusChanged;
    }

    /// <summary>How much history the graph shows.</summary>
    public TimeSpan Window { get; } = TimeSpan.FromMinutes(3);

    /// <summary>Oldest first. A new list on every change, so the graph can simply take it.</summary>
    [ObservableProperty] private IReadOnlyList<SpeedSample> _samples = [];

    [ObservableProperty] private bool _hasHistory;

    /// <summary>The top of the graph in bytes per second: a round number (1, 2 or 5 × 10ⁿ) above the visible peak.</summary>
    [ObservableProperty] private long _scale = MinimumScale;

    [ObservableProperty] private string _scaleText = ByteFormatter.FormatRate(MinimumScale);
    [ObservableProperty] private string _downloadText = "";
    [ObservableProperty] private string _uploadText = "";

    public void Dispose()
    {
        _feed.StatsUpdated -= OnStatsUpdated;
        _connection.StatusChanged -= OnStatusChanged;
    }

    /// <summary>The smallest 1, 2 or 5 × 10ⁿ that is at least <paramref name="value"/> (and the minimum scale).</summary>
    public static long NiceScale(long value)
    {
        long step = 1;
        while (true)
        {
            foreach (var factor in (ReadOnlySpan<long>)[1, 2, 5])
            {
                var candidate = factor * step;
                if (candidate >= Math.Max(value, MinimumScale)) return candidate;
            }

            step *= 10;
        }
    }

    /// <summary>Older samples, as if the app had been running (the README screenshots); shown from the next poll.</summary>
    internal void Seed(IEnumerable<SpeedSample> samples) => _history.InsertRange(0, samples);

    private void OnStatusChanged(object? sender, EventArgs e)
    {
        if (_connection.Status.State != ConnectionState.Connected)
            Clear();
    }

    private void OnStatsUpdated(object? sender, SessionStats stats)
    {
        var now = _time.GetUtcNow();
        _history.Add(new SpeedSample(now, Math.Max(stats.DownloadSpeed, 0), Math.Max(stats.UploadSpeed, 0)));

        // Keep one sample older than the window, so the line runs on to the left edge instead of stopping short.
        var start = now - Window;
        var drop = 0;
        while (drop + 1 < _history.Count && _history[drop + 1].Time <= start) drop++;
        _history.RemoveRange(0, drop);

        // The scale follows what is inside the window; the line to the edge sample may run off the top for a moment.
        long peak = 0;
        foreach (var sample in _history)
            if (sample.Time > start) peak = Math.Max(peak, Math.Max(sample.Download, sample.Upload));

        Scale = NiceScale(peak);
        ScaleText = ByteFormatter.FormatRate(Scale);
        DownloadText = $"↓ {ByteFormatter.FormatRate(stats.DownloadSpeed)}";
        UploadText = $"↑ {ByteFormatter.FormatRate(stats.UploadSpeed)}";
        Samples = _history.ToArray();
        HasHistory = true;
    }

    private void Clear()
    {
        _history.Clear();
        Samples = [];
        HasHistory = false;
        Scale = MinimumScale;
        ScaleText = ByteFormatter.FormatRate(MinimumScale);
        DownloadText = UploadText = "";
    }
}
