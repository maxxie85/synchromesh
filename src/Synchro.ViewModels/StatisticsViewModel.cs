using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Formatting;

namespace Synchro.ViewModels;

/// <summary>One line of the Statistics table: this session and all time side by side.</summary>
public sealed partial class StatisticsRow(string label) : ObservableObject
{
    public string Label { get; } = label;

    [ObservableProperty] private string _current = "";
    [ObservableProperty] private string _cumulative = "";
}

/// <summary>Server-wide totals from `session_stats` for the sidebar, refreshed with every poll.</summary>
public sealed partial class StatisticsViewModel : ObservableObject, IDisposable
{
    private readonly ITorrentFeed _feed;
    private readonly StatisticsRow _downloaded = new("Downloaded");
    private readonly StatisticsRow _uploaded = new("Uploaded");
    private readonly StatisticsRow _ratio = new("Ratio");
    private readonly StatisticsRow _filesAdded = new("Added");
    private readonly StatisticsRow _active = new("Active");
    private readonly StatisticsRow _sessions = new("Sessions");

    public StatisticsViewModel(ITorrentFeed feed)
    {
        _feed = feed;
        Rows = [_downloaded, _uploaded, _ratio, _filesAdded, _active, _sessions];
        _feed.StatsUpdated += OnStatsUpdated;
    }

    public IReadOnlyList<StatisticsRow> Rows { get; }

    [ObservableProperty] private bool _hasStatistics;

    public void Dispose() => _feed.StatsUpdated -= OnStatsUpdated;

    private void OnStatsUpdated(object? sender, SessionStats stats)
    {
        if (stats.Current is not { } current || stats.Cumulative is not { } cumulative) return;

        Fill(_downloaded, t => ByteFormatter.FormatSize(t.DownloadedBytes));
        Fill(_uploaded, t => ByteFormatter.FormatSize(t.UploadedBytes));
        Fill(_ratio, t => t.DownloadedBytes > 0
            ? ((double)t.UploadedBytes / t.DownloadedBytes).ToString("0.00", CultureInfo.CurrentCulture)
            : DurationFormatter.Unknown);
        Fill(_filesAdded, t => t.FilesAdded.ToString("N0", CultureInfo.CurrentCulture));
        Fill(_active, t => DurationFormatter.FormatEta(t.SecondsActive));
        _sessions.Current = "";
        _sessions.Cumulative = cumulative.SessionCount.ToString("N0", CultureInfo.CurrentCulture);
        HasStatistics = true;

        void Fill(StatisticsRow row, Func<StatsTotals, string> format)
        {
            row.Current = format(current);
            row.Cumulative = format(cumulative);
        }
    }
}
