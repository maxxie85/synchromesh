using Synchro.Core.Torrents;
using Synchro.ViewModels;
using Xunit;
using static Synchro.Tests.TestData;

namespace Synchro.Tests;

public class TorrentItemViewModelTests
{
    [Theory]
    [InlineData(TorrentStatus.Downloading, 0.5, false, "Downloading")]
    [InlineData(TorrentStatus.Downloading, 0.5, true, "Stalled")]
    [InlineData(TorrentStatus.Seeding, 1.0, false, "Seeding")]
    [InlineData(TorrentStatus.Stopped, 0.3, false, "Paused")]
    [InlineData(TorrentStatus.Stopped, 1.0, false, "Finished")]
    [InlineData(TorrentStatus.QueuedToDownload, 0.0, false, "Queued")]
    [InlineData(TorrentStatus.Verifying, 0.1, false, "Verifying")]
    public void Describes_the_status(TorrentStatus status, double done, bool stalled, string expected)
        => Assert.Equal(expected, new TorrentItemViewModel(MakeTorrent("a", status: status, done: done, stalled: stalled)).StatusText);

    [Fact]
    public void Progress_is_a_percentage_clamped_to_0_100()
    {
        Assert.Equal(50, new TorrentItemViewModel(MakeTorrent("a", done: 0.5)).Progress);
        Assert.Equal(100, new TorrentItemViewModel(MakeTorrent("a", done: 1.7)).Progress);
    }

    [Fact]
    public void Shows_eta_only_while_downloading()
    {
        Assert.Equal("1m 0s", new TorrentItemViewModel(MakeTorrent("a", status: TorrentStatus.Downloading, eta: 60)).EtaText);
        Assert.Equal("", new TorrentItemViewModel(MakeTorrent("a", status: TorrentStatus.Seeding, eta: 60)).EtaText);
    }

    [Fact]
    public void Hides_idle_rates_and_unknown_ratio()
    {
        var idle = MakeTorrent("a") with { RateDownload = 0, RateUpload = 0, UploadRatio = -1 };

        var vm = new TorrentItemViewModel(idle);

        Assert.Equal("", vm.DownloadText);
        Assert.Equal("", vm.UploadText);
        Assert.Equal("", vm.RatioText);
    }

    [Fact]
    public void Surfaces_the_tracker_or_local_error()
    {
        var vm = new TorrentItemViewModel(MakeTorrent("a", error: 2));

        Assert.True(vm.HasError);
        Assert.Equal("tracker said no", vm.ErrorText);
    }

    [Fact]
    public void Applying_a_new_snapshot_raises_change_notifications_only_for_what_changed()
    {
        var vm = new TorrentItemViewModel(MakeTorrent("a", done: 0.2));
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Apply(MakeTorrent("a", done: 0.4));

        Assert.Contains(nameof(TorrentItemViewModel.Progress), changed);
        Assert.DoesNotContain(nameof(TorrentItemViewModel.Name), changed);
    }
}
