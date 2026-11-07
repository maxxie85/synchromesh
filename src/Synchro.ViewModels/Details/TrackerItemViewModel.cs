using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Synchro.Core.Settings;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Formatting;

namespace Synchro.ViewModels.Details;

public sealed partial class TrackerItemViewModel(int id) : ObservableObject
{
    public int Id { get; } = id;

    /// <summary>Zero-based tier, as the server numbers them.</summary>
    public int Tier { get; private set; }

    // Raw values for sorting; negative counts and a null time are unknown.
    internal TimeSpan? NextUpdate { get; private set; }
    internal int Seeders { get; private set; }
    internal int Leechers { get; private set; }
    internal int Downloads { get; private set; }

    /// <summary>Column keys, as the view's columns are tagged.</summary>
    public static class Columns
    {
        public const string Tier = "Tier", Address = "Address", Status = "Status", NextUpdate = "NextUpdate",
            Seeders = "Seeders", Leechers = "Leechers", Downloads = "Downloads";
    }

    public static TableSort DefaultSort { get; } = new(Columns.Tier, false);

    public static bool StartsDescending(string column) => column is Columns.Seeders or Columns.Leechers or Columns.Downloads;

    /// <summary>The rows' order for <paramref name="sort"/>; ties by tier, then the server's tracker id.</summary>
    public static Comparison<TrackerItemViewModel> Order(TableSort sort)
    {
        return TableSorting.Comparison<TrackerItemViewModel>(sort,
        column => column switch
        {
            Columns.Tier => (a, b) => a.Tier.CompareTo(b.Tier),
            Columns.Address => (a, b) => NaturalStringComparer.Instance.Compare(a.Address, b.Address),
            Columns.Status => (a, b) => string.Compare(a.StatusText, b.StatusText, StringComparison.CurrentCultureIgnoreCase),
            Columns.NextUpdate => (a, b) => TableSorting.KnownFirst(a.NextUpdate is not null, b.NextUpdate is not null,
                sort.Descending, () => a.NextUpdate!.Value.CompareTo(b.NextUpdate!.Value)),
            Columns.Seeders => Count(t => t.Seeders),
            Columns.Leechers => Count(t => t.Leechers),
            Columns.Downloads => Count(t => t.Downloads),
            _ => null,
        },
        (a, b) => a.Tier != b.Tier ? a.Tier.CompareTo(b.Tier) : a.Id.CompareTo(b.Id));

        Comparison<TrackerItemViewModel> Count(Func<TrackerItemViewModel, int> value) => (a, b) =>
            TableSorting.KnownFirst(value(a) >= 0, value(b) >= 0, sort.Descending, () => value(a).CompareTo(value(b)));
    }

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
        NextUpdate = t.NextAnnounceTime is { } next && next > now && t.AnnounceState != TrackerAnnounceState.Active
            ? next - now
            : null;
        NextUpdateText = NextUpdate is { } wait ? DurationFormatter.FormatEta((long)wait.TotalSeconds) : "";
        Seeders = t.SeederCount;
        Leechers = t.LeecherCount;
        Downloads = t.DownloadCount;
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
