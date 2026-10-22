using System.Globalization;
using Material.Icons;
using Synchro.Core.Torrents;
using Synchro.Desktop.Converters;
using Synchro.ViewModels;
using Xunit;

namespace Synchro.Tests;

public class IconConvertersTests
{
    private static object? Convert(Avalonia.Data.Converters.IValueConverter converter, object value) =>
        converter.Convert(value, typeof(object), null, CultureInfo.InvariantCulture);

    [Fact]
    public void Every_filter_has_its_own_icon()
    {
        var icons = Enum.GetValues<TorrentFilter>().Select(f => Convert(IconConverters.Filter, f)).ToList();

        Assert.Equal(icons.Count, icons.Distinct().Count());
    }

    [Theory]
    [InlineData(TorrentStatus.Downloading, MaterialIconKind.Download)]
    [InlineData(TorrentStatus.Seeding, MaterialIconKind.Upload)]
    [InlineData(TorrentStatus.Stopped, MaterialIconKind.Pause)]
    [InlineData(TorrentStatus.Verifying, MaterialIconKind.Sync)]
    [InlineData(TorrentStatus.QueuedToDownload, MaterialIconKind.ClockOutline)]
    public void Status_icons_match_the_state(TorrentStatus status, MaterialIconKind expected) =>
        Assert.Equal(expected, Convert(IconConverters.Status, status));

    [Theory]
    [InlineData(TorrentStatus.Downloading, true, false)]
    [InlineData(TorrentStatus.Verifying, true, false)]
    [InlineData(TorrentStatus.Seeding, false, true)]
    [InlineData(TorrentStatus.Stopped, false, false)]
    [InlineData(TorrentStatus.QueuedToSeed, false, false)]
    public void Status_colour_groups(TorrentStatus status, bool downloading, bool seeding)
    {
        Assert.Equal(downloading, Convert(StatusConverters.IsDownloading, status));
        Assert.Equal(seeding, Convert(StatusConverters.IsSeeding, status));
    }
}
