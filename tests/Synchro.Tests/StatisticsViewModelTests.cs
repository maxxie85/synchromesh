using NSubstitute;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.ViewModels;
using Synchro.ViewModels.Formatting;
using Xunit;

namespace Synchro.Tests;

public class StatisticsViewModelTests
{
    private readonly ITorrentFeed _feed = Substitute.For<ITorrentFeed>();

    private void Publish(SessionStats stats) =>
        _feed.StatsUpdated += Raise.Event<EventHandler<SessionStats>>(_feed, stats);

    private static StatisticsRow Row(StatisticsViewModel vm, string label) => vm.Rows.Single(r => r.Label == label);

    [Fact]
    public void Shows_this_session_and_all_time_side_by_side()
    {
        var vm = new StatisticsViewModel(_feed);

        Publish(new SessionStats(5, 1, 0, 0, 0,
            Current: new StatsTotals(2_000_000_000, 4_000_000_000, 3, 90_000, 1),
            Cumulative: new StatsTotals(2_000_000_000_000, 9_000_000_000_000, 6456, 108_086_400, 42)));

        Assert.True(vm.HasStatistics);
        Assert.Equal(ByteFormatter.FormatSize(2_000_000_000), Row(vm, "Downloaded").Current);
        Assert.Equal(ByteFormatter.FormatSize(9_000_000_000_000), Row(vm, "Uploaded").Cumulative);
        Assert.Equal(2.0.ToString("0.00"), Row(vm, "Ratio").Current);
        Assert.Equal(6456.ToString("N0"), Row(vm, "Added").Cumulative);
        Assert.Equal("1d 1h", Row(vm, "Active").Current);
        Assert.Equal("1251d 0h", Row(vm, "Active").Cumulative);
        Assert.Equal("", Row(vm, "Sessions").Current);
        Assert.Equal("42", Row(vm, "Sessions").Cumulative);
    }

    [Fact]
    public void Nothing_downloaded_yet_has_no_ratio()
    {
        var vm = new StatisticsViewModel(_feed);

        Publish(new SessionStats(0, 0, 0, 0, 0, new StatsTotals(0, 0, 0, 10, 1), new StatsTotals(0, 0, 0, 10, 1)));

        Assert.Equal(DurationFormatter.Unknown, Row(vm, "Ratio").Current);
    }

    [Fact]
    public void Without_totals_from_the_server_it_keeps_waiting()
    {
        var vm = new StatisticsViewModel(_feed);

        Publish(new SessionStats(1, 0, 0, 0, 0));

        Assert.False(vm.HasStatistics);
    }
}
