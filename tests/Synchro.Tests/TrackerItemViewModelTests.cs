using Synchro.Core.Torrents;
using Synchro.ViewModels.Details;
using Xunit;
using static Synchro.Tests.TestData;

namespace Synchro.Tests;

public class TrackerItemViewModelTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static TrackerItemViewModel Apply(TrackerInfo t)
    {
        var vm = new TrackerItemViewModel(t.Id);
        vm.Apply(t, Now);
        return vm;
    }

    [Fact]
    public void A_successful_announce_is_working_with_the_time_until_the_next()
    {
        var vm = Apply(MakeTracker(1, next: Now.AddMinutes(32).AddSeconds(28)));

        Assert.Equal("Working", vm.StatusText);
        Assert.True(vm.IsWorking);
        Assert.False(vm.HasError);
        Assert.Equal("32m 28s", vm.NextUpdateText);
        Assert.Equal("1", vm.TierText);
    }

    [Fact]
    public void A_failed_announce_shows_the_trackers_reason_as_a_sentence()
    {
        var vm = Apply(MakeTracker(1, succeeded: false, result: "could not connect to tracker"));

        Assert.Equal("Could not connect to tracker", vm.StatusText);
        Assert.True(vm.HasError);
    }

    [Theory]
    [InlineData(TrackerAnnounceState.Active, true, false, false, "Announcing…")]
    [InlineData(TrackerAnnounceState.Waiting, true, false, true, "Timed out")]
    [InlineData(TrackerAnnounceState.Queued, false, false, false, "Queued")]
    [InlineData(TrackerAnnounceState.Inactive, false, false, false, "Not contacted yet")]
    public void Other_states_have_plain_words(TrackerAnnounceState state, bool announced, bool succeeded, bool timedOut, string expected)
    {
        Assert.Equal(expected, Apply(MakeTracker(1, state, announced, succeeded, timedOut)).StatusText);
    }

    [Fact]
    public void Unknown_counts_and_a_past_next_announce_are_left_blank()
    {
        var vm = Apply(MakeTracker(1, next: Now.AddSeconds(-5), seeders: -1));

        Assert.Equal("", vm.SeedersText);
        Assert.Equal("", vm.NextUpdateText);
    }
}
