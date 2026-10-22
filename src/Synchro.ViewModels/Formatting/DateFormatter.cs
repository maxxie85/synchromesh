using System.Globalization;

namespace Synchro.ViewModels.Formatting;

public static class DateFormatter
{
    /// <summary>Local date and time in the user's short format; <see cref="DurationFormatter.Unknown"/> for none.</summary>
    public static string Format(DateTimeOffset? value, CultureInfo? culture = null)
        => value is { } v ? v.ToLocalTime().ToString("g", culture ?? CultureInfo.CurrentCulture) : DurationFormatter.Unknown;
}
