using NSubstitute;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.ViewModels;
using Synchro.ViewModels.Formatting;
using Xunit;

namespace Synchro.Tests;

public class SpeedHistoryViewModelTests
{
    private readonly ITorrentFeed _feed = Substitute.For<ITorrentFeed>();
    private readonly IConnectionService _connection = Substitute.For<IConnectionService>();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));

    private SpeedHistoryViewModel Create() => new(_feed, _connection, _clock);

    private void Publish(long download, long upload) =>
        _feed.StatsUpdated += Raise.Event<EventHandler<SessionStats>>(_feed, new SessionStats(1, 1, 0, download, upload));

    private void Publish(ConnectionState state)
    {
        _connection.Status.Returns(new ConnectionStatus(state));
        _connection.StatusChanged += Raise.Event();
    }

    [Fact]
    public void Each_poll_adds_a_sample_and_shows_the_current_speeds()
    {
        var vm = Create();

        Publish(1_500_000, 300_000);
        _clock.Advance(TimeSpan.FromSeconds(2));
        Publish(2_000_000, 250_000);

        Assert.True(vm.HasHistory);
        Assert.Equal([1_500_000, 2_000_000], vm.Samples.Select(s => s.Download));
        Assert.Equal(TimeSpan.FromSeconds(2), vm.Samples[1].Time - vm.Samples[0].Time);
        Assert.Equal($"↓ {ByteFormatter.FormatRate(2_000_000)}", vm.DownloadText);
        Assert.Equal($"↑ {ByteFormatter.FormatRate(250_000)}", vm.UploadText);
    }

    [Fact]
    public void Samples_older_than_the_window_are_dropped_except_the_one_that_reaches_the_left_edge()
    {
        var vm = Create();
        for (var i = 0; i < 200; i++)
        {
            Publish(i, 0);
            _clock.Advance(TimeSpan.FromSeconds(2));
        }

        var start = vm.Samples[^1].Time - vm.Window;
        Assert.True(vm.Samples[0].Time <= start);
        Assert.True(vm.Samples[1].Time > start);
        Assert.Equal(91, vm.Samples.Count); // 90 polls in 3 minutes, plus that one
    }

    [Fact]
    public void The_scale_is_a_round_number_above_the_peak_of_either_line()
    {
        var vm = Create();

        Publish(1_200_000, 3_400_000);

        Assert.Equal(5_000_000, vm.Scale);
        Assert.Equal(ByteFormatter.FormatRate(5_000_000), vm.ScaleText);
    }

    [Fact]
    public void The_scale_shrinks_again_once_the_peak_leaves_the_window()
    {
        var vm = Create();
        Publish(40_000_000, 0);

        _clock.Advance(vm.Window + TimeSpan.FromSeconds(1));
        Publish(10, 0);
        _clock.Advance(TimeSpan.FromSeconds(2));
        Publish(800_000, 0);

        Assert.Equal(1_000_000, vm.Scale);
    }

    [Theory]
    [InlineData(0, SpeedHistoryViewModel.MinimumScale)]
    [InlineData(100_000, 100_000)]
    [InlineData(100_001, 200_000)]
    [InlineData(2_000_001, 5_000_000)]
    [InlineData(5_000_001, 10_000_000)]
    [InlineData(123_000_000, 200_000_000)]
    public void Nice_scale_picks_one_two_or_five_times_a_power_of_ten(long value, long expected) =>
        Assert.Equal(expected, SpeedHistoryViewModel.NiceScale(value));

    [Theory]
    [InlineData(ConnectionState.Connecting)] // switching servers
    [InlineData(ConnectionState.Disconnected)]
    [InlineData(ConnectionState.Failed)] // a gap would be drawn as a straight line across it
    public void The_history_is_cleared_when_the_connection_is_not_up(ConnectionState state)
    {
        var vm = Create();
        Publish(1_000_000, 1_000_000);

        Publish(state);

        Assert.False(vm.HasHistory);
        Assert.Empty(vm.Samples);
        Assert.Equal("", vm.DownloadText);
        Assert.Equal(SpeedHistoryViewModel.MinimumScale, vm.Scale);
    }

    [Fact]
    public void Staying_connected_keeps_the_history()
    {
        var vm = Create();
        Publish(1_000_000, 0);

        Publish(ConnectionState.Connected);

        Assert.Single(vm.Samples);
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
