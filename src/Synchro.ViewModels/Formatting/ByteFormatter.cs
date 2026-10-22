using System.Globalization;

namespace Synchro.ViewModels.Formatting;

/// <summary>Decimal (SI) units, which is what macOS Finder and Transmission use: 1 MB = 1,000,000 bytes.</summary>
public static class ByteFormatter
{
    private static readonly string[] Units = ["B", "kB", "MB", "GB", "TB", "PB"];

    public static string FormatSize(long bytes, CultureInfo? culture = null)
    {
        double value = Math.Max(bytes, 0);
        var unit = 0;
        while (value >= 1000 && unit < Units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        // Whole numbers for bytes, kB and anything from 100 up; one decimal in between (4.3 MB, 12.4 GB).
        var format = unit <= 1 || value >= 100 ? "0" : "0.0";
        return $"{value.ToString(format, culture ?? CultureInfo.CurrentCulture)} {Units[unit]}";
    }

    public static string FormatRate(long bytesPerSecond, CultureInfo? culture = null)
        => $"{FormatSize(bytesPerSecond, culture)}/s";
}
