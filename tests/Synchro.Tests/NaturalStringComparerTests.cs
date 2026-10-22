using Synchro.ViewModels.Formatting;
using Xunit;

namespace Synchro.Tests;

public class NaturalStringComparerTests
{
    private static string[] Sorted(params string[] names) =>
        names.OrderBy(n => n, NaturalStringComparer.Instance).ToArray();

    [Fact]
    public void Numbers_sort_by_value()
    {
        Assert.Equal(new[] { "s1.jpg", "s2.jpg", "s10.jpg", "s100.jpg" }, Sorted("s10.jpg", "s100.jpg", "s2.jpg", "s1.jpg"));
    }

    [Fact]
    public void Text_ignores_case_and_numbers_inside_names_compare_per_run()
    {
        Assert.Equal(new[] { "Disc 1", "disc 2", "Disc 10", "Extras" }, Sorted("Extras", "Disc 10", "disc 2", "Disc 1"));
        Assert.Equal(new[] { "S01E02", "S01E10", "S02E01" }, Sorted("S02E01", "S01E10", "S01E02"));
    }

    [Fact]
    public void Leading_zeros_and_very_long_numbers_are_handled()
    {
        Assert.Equal(new[] { "file007", "file8", "file99999999999999999999999" },
            Sorted("file99999999999999999999999", "file8", "file007"));
    }

    [Fact]
    public void A_prefix_sorts_first_and_the_order_is_stable()
    {
        Assert.Equal(new[] { "a", "a1", "ab" }, Sorted("ab", "a1", "a"));
        Assert.NotEqual(0, NaturalStringComparer.Instance.Compare("a", "A"));
        Assert.Equal(0, NaturalStringComparer.Instance.Compare("same", "same"));
    }
}
