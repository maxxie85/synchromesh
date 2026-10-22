using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Formatting;

namespace Synchro.ViewModels.Details;

public sealed partial class TrackerItemViewModel(int id) : ObservableObject
{
    public int Id { get; } = id;

    /// <summary>Zero-based tier, as the server numbers them.</summary>
    public int Tier { get; private set; }

    [ObservableProperty] private string _tierText = "";
    [ObservableProperty] private string _address = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isWorking;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _nextUpdateText = "";
    [ObservableProperty] private string _seedersText = "";
    [ObservableProperty] private string _leechersText = "";
    [ObservableProperty] private string _downloadsText = "";

    public void Apply(TrackerInfo t, DateTimeOffset now)
    {
        Tier = t.Tier;
        TierText = (t.Tier + 1).ToString(CultureInfo.CurrentCulture);
        Address = t.Announce;
        StatusText = DescribeStatus(t);
        IsWorking = t.HasAnnounced && t.LastAnnounceSucceeded && t.AnnounceState != TrackerAnnounceState.Active;
        HasError = t.HasAnnounced && !t.LastAnnounceSucceeded && t.AnnounceState != TrackerAnnounceState.Active;
        NextUpdateText = t.NextAnnounceTime is { } next && next > now && t.AnnounceState != TrackerAnnounceState.Active
            ? DurationFormatter.FormatEta((long)(next - now).TotalSeconds)
            : "";
        SeedersText = Count(t.SeederCount);
        LeechersText = Count(t.LeecherCount);
        DownloadsText = Count(t.DownloadCount);
    }

    private static string DescribeStatus(TrackerInfo t)
    {
        if (t.AnnounceState == TrackerAnnounceState.Active) return "Announcing…";
        if (t.IsBackup) return "Backup";

        if (t.HasAnnounced)
        {
            if (t.LastAnnounceSucceeded) return "Working";
            if (t.LastAnnounceTimedOut) return "Timed out";
            return string.IsNullOrWhiteSpace(t.LastAnnounceResult) ? "Failed" : Sentence(t.LastAnnounceResult);
        }

        return t.AnnounceState switch
        {
            TrackerAnnounceState.Queued => "Queued",
            TrackerAnnounceState.Waiting => "Waiting",
            _ => "Not contacted yet",
        };
    }

    // Trackers and libtransmission phrase errors in lower case ("could not connect to tracker").
    private static string Sentence(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string Count(int value) => value < 0 ? "" : value.ToString("N0", CultureInfo.CurrentCulture);
}
