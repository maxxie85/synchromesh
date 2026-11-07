using Synchro.Core.Settings;

namespace Synchro.ViewModels;

/// <summary>Header clicks for the details tables: the same rules as the torrent list (see <see cref="TorrentSorting"/>).</summary>
public static class TableSorting
{
    /// <summary>The next sort after clicking <paramref name="column"/>: the other direction, or that column's start.</summary>
    /// <param name="startsDescending">Columns of amounts (sizes, speeds, counts) start biggest first.</param>
    public static TableSort Toggle(TableSort current, string column, Func<string, bool> startsDescending) =>
        current.Column == column
            ? current with { Descending = !current.Descending }
            : new TableSort(column, startsDescending(column));

    /// <summary>A column's order in the chosen direction, then <paramref name="tieBreak"/> (always ascending).</summary>
    public static Comparison<T> Comparison<T>(TableSort sort, Func<string, Comparison<T>?> byColumn, Comparison<T> tieBreak)
    {
        var column = byColumn(sort.Column);
        return (a, b) =>
        {
            var result = column is null ? 0 : column(a, b);
            if (sort.Descending) result = -result;
            return result != 0 ? result : tieBreak(a, b);
        };
    }

    /// <summary>Compares values that may be unknown: unknown ones last whichever the direction.</summary>
    public static int KnownFirst(bool aKnown, bool bKnown, bool descending, Func<int> compare)
    {
        if (aKnown && bKnown) return compare();
        var unknownLast = bKnown.CompareTo(aKnown);
        return descending ? -unknownLast : unknownLast; // undone again by the direction
    }
}
