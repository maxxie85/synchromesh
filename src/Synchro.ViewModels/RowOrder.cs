namespace Synchro.ViewModels;

/// <summary>
/// Brings a list's rows into a target order with as few changes as possible. Rows that are out of place are removed
/// and inserted again, so the list keeps its other rows and its scroll position; with many changes the whole list is
/// replaced in one Reset. Never Move: the DataGrid's collection view ignores Move notifications, so it would keep
/// drawing the old order.
/// </summary>
public static class RowOrder
{
    /// <summary>More remove-and-insert steps than this, and one Reset is cheaper.</summary>
    public const int MaxSteps = 16;

    /// <summary>Makes <paramref name="rows"/> equal to <paramref name="target"/>, which may also add or drop rows.</summary>
    /// <returns>True when anything changed.</returns>
    public static bool Apply<T>(BulkObservableCollection<T> rows, IReadOnlyList<T> target) where T : class
    {
        if (rows.Count == target.Count && rows.SequenceEqual(target, ReferenceEqualityComparer.Instance)) return false;

        if (Steps(rows.ToList(), target) is not { } steps)
        {
            rows.ReplaceAll(target);
            return true;
        }

        foreach (var step in steps)
            step(rows);
        return true;
    }

    // The changes, worked out on a copy first: one row that overtakes the others is one step, even though every row
    // below it shifts a place. Null when there are more than MaxSteps.
    private static List<Action<IList<T>>>? Steps<T>(List<T> current, IReadOnlyList<T> target) where T : class
    {
        var wanted = new HashSet<T>(target, ReferenceEqualityComparer.Instance);
        var steps = new List<Action<IList<T>>>();

        // Rows that are gone.
        for (var i = current.Count - 1; i >= 0; i--)
        {
            if (wanted.Contains(current[i])) continue;
            if (steps.Count == MaxSteps) return null;
            var at = i;
            current.RemoveAt(at);
            steps.Add(rows => rows.RemoveAt(at));
        }

        // Rows in the wrong place or new.
        for (var i = 0; i < target.Count; i++)
        {
            if (i < current.Count && ReferenceEquals(current[i], target[i])) continue;
            if (steps.Count == MaxSteps) return null;

            var item = target[i];
            var from = current.FindIndex(i, c => ReferenceEquals(c, item));
            var to = i;
            if (from >= 0)
            {
                current.RemoveAt(from);
                current.Insert(to, item);
                steps.Add(rows => { rows.RemoveAt(from); rows.Insert(to, item); });
            }
            else
            {
                current.Insert(to, item);
                steps.Add(rows => rows.Insert(to, item));
            }
        }

        return steps;
    }
}
