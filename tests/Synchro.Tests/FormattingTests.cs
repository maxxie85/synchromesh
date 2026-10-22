using System.Globalization;
using Synchro.ViewModels.Formatting;
using Xunit;

namespace Synchro.Tests;

public class FormattingTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(999L, "999 B")]
    [InlineData(4_300_000L, "4.3 MB")]
    [InlineData(312_000_000L, "312 MB")]
    [InlineData(1_500_000_000L, "1.5 GB")]
    [InlineData(2_000_000_000_000L, "2.0 TB")]
    public void Formats_sizes_with_decimal_units(long bytes, string expected)
        => Assert.Equal(expected, ByteFormatter.FormatSize(bytes, Invariant));

    [Fact]
    public void Formats_rates_per_second()
        => Assert.Equal("4.3 MB/s", ByteFormatter.FormatRate(4_300_000, Invariant));

    [Theory]
    [InlineData(-1L, "—")]
    [InlineData(-2L, "—")]
    [InlineData(45L, "45s")]
    [InlineData(725L, "12m 5s")]
    [InlineData(11_520L, "3h 12m")]
    [InlineData(187_200L, "2d 4h")]
    public void Formats_eta_with_two_units(long seconds, string expected)
        => Assert.Equal(expected, DurationFormatter.FormatEta(seconds));
}
