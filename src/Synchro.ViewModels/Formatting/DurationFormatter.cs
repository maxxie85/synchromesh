namespace Synchro.ViewModels.Formatting;

public static class DurationFormatter
{
    public const string Unknown = "—";

    /// <summary>Two most significant units: "2d 4h", "3h 12m", "12m 5s", "45s". Negative means unknown.</summary>
    public static string FormatEta(long seconds)
    {
        if (seconds < 0) return Unknown;

        var span = TimeSpan.FromSeconds(seconds);
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m {span.Seconds}s";
        return $"{span.Seconds}s";
    }
}
