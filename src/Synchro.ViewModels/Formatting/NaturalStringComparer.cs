namespace Synchro.ViewModels.Formatting;

/// <summary>
/// Compares names the way Finder sorts them: runs of digits by their value ("s2" before "s10"), the rest
/// case-insensitively. Implemented here rather than with CompareOptions.NumericOrdering, which needs ICU and is
/// silently ignored without it (some Linux machines and CI runners).
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var startX = i;
                var startY = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;

                var byValue = CompareDigits(x.AsSpan(startX, i - startX), y.AsSpan(startY, j - startY));
                if (byValue != 0) return byValue;
                continue;
            }

            var startText = i;
            var startOther = j;
            while (i < x.Length && !char.IsAsciiDigit(x[i])) i++;
            while (j < y.Length && !char.IsAsciiDigit(y[j])) j++;

            var byText = x.AsSpan(startText, i - startText)
                .CompareTo(y.AsSpan(startOther, j - startOther), StringComparison.CurrentCultureIgnoreCase);
            if (byText != 0) return byText;
        }

        // One name is a prefix of the other: the shorter comes first. Ties fall back to an exact comparison
        // so the order is stable ("a" and "A").
        var byLength = (x.Length - i).CompareTo(y.Length - j);
        return byLength != 0 ? byLength : string.CompareOrdinal(x, y);
    }

    // Numbers of any length (no overflow): ignore leading zeros, then the longer number is larger.
    private static int CompareDigits(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        a = a.TrimStart('0');
        b = b.TrimStart('0');
        if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
        return a.SequenceCompareTo(b);
    }
}
