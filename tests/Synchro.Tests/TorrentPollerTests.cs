using System.Collections.Concurrent;
using NSubstitute;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Xunit;

namespace Synchro.Tests;

public class TorrentPollerTests
{
    private static readonly SessionStats Stats = new(1, 1, 0, 0, 0);

    private static ITransmissionClient ClientThatSucceeds()
    {
        var client = Substitute.For<ITransmissionClient>();
        client.GetTorrentsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new TorrentUpdate([], [], IsFullRefresh: !call.Arg<bool>())));
        client.GetSessionStatsAsync(Arg.Any<CancellationToken>()).Returns(Stats);
        return client;
    }

    [Fact]
    public async Task Starts_with_a_full_refresh_then_polls_deltas()
    {
        var updates = new ConcurrentQueue<TorrentUpdate>();
        using var poller = new TorrentPoller(TimeSpan.FromMilliseconds(5));
        poller.TorrentsUpdated += (_, u) => updates.Enqueue(u);

        poller.Start(ClientThatSucceeds());
        await TestWait.UntilAsync(() => updates.Count >= 3);
        await poller.StopAsync();

        var list = updates.ToList();
        Assert.True(list[0].IsFullRefresh);
        Assert.False(list[1].IsFullRefresh);
        Assert.False(list[2].IsFullRefresh);
    }

    [Fact]
    public async Task Raises_stats_with_every_poll()
    {
        var stats = new ConcurrentQueue<SessionStats>();
        using var poller = new TorrentPoller(TimeSpan.FromMilliseconds(5));
        poller.StatsUpdated += (_, s) => stats.Enqueue(s);

        poller.Start(ClientThatSucceeds());
        await TestWait.UntilAsync(() => stats.Count >= 2);
        await poller.StopAsync();

        Assert.All(stats, s => Assert.Equal(Stats, s));
    }

    [Fact]
    public async Task Reports_loss_once_then_recovery_and_resyncs_with_a_full_refresh()
    {
        var calls = 0;
        var client = Substitute.For<ITransmissionClient>();
        client.GetTorrentsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var n = Interlocked.Increment(ref calls);
            return n is 2 or 3
                ? Task.FromException<TorrentUpdate>(new HttpRequestException("boom"))
                : Task.FromResult(new TorrentUpdate([], [], IsFullRefresh: !call.Arg<bool>()));
        });
        client.GetSessionStatsAsync(Arg.Any<CancellationToken>()).Returns(Stats);

        var updates = new ConcurrentQueue<TorrentUpdate>();
        var lost = new ConcurrentQueue<string>();
        var restored = 0;
        using var poller = new TorrentPoller(TimeSpan.FromMilliseconds(5));
        poller.TorrentsUpdated += (_, u) => updates.Enqueue(u);
        poller.ConnectionLost += (_, reason) => lost.Enqueue(reason);
        poller.ConnectionRestored += (_, _) => Interlocked.Increment(ref restored);

        poller.Start(client);
        await TestWait.UntilAsync(() => updates.Count >= 3);
        await poller.StopAsync();

        Assert.Equal(new[] { "boom" }, lost.ToArray());   // two failed polls, one notification
        Assert.Equal(1, restored);
        var list = updates.ToList();
        Assert.True(list[0].IsFullRefresh);
        Assert.True(list[1].IsFullRefresh);       // first success after the outage re-syncs everything
        Assert.False(list[2].IsFullRefresh);
    }

    [Fact]
    public async Task Stop_ends_polling()
    {
        var client = ClientThatSucceeds();
        using var poller = new TorrentPoller(TimeSpan.FromMilliseconds(5));
        var updates = 0;
        poller.TorrentsUpdated += (_, _) => Interlocked.Increment(ref updates);

        poller.Start(client);
        await TestWait.UntilAsync(() => Volatile.Read(ref updates) >= 2);
        await poller.StopAsync();
        var afterStop = Volatile.Read(ref updates);
        await Task.Delay(60);

        Assert.Equal(afterStop, Volatile.Read(ref updates));
    }

    [Fact]
    public async Task A_refresh_request_polls_again_at_once_with_a_full_refresh()
    {
        var updates = new ConcurrentQueue<TorrentUpdate>();
        using var poller = new TorrentPoller(TimeSpan.FromHours(1)); // only the request can trigger a second poll
        poller.TorrentsUpdated += (_, u) => updates.Enqueue(u);

        poller.Start(ClientThatSucceeds());
        await TestWait.UntilAsync(() => updates.Count >= 1);

        poller.RequestRefresh();
        await TestWait.UntilAsync(() => updates.Count >= 2);
        await poller.StopAsync();

        Assert.True(updates.ToList()[1].IsFullRefresh);
    }

    [Fact]
    public async Task Details_are_fetched_only_while_a_torrent_is_watched()
    {
        var client = ClientThatSucceeds();
        var details = TestData.MakeDetails("abc");
        client.GetTorrentDetailsAsync("abc", Arg.Any<DetailFiles>(), Arg.Any<CancellationToken>()).Returns(details);
        var updates = new ConcurrentQueue<TorrentDetailsUpdate>();
        using var poller = new TorrentPoller(TimeSpan.FromMilliseconds(5));
        poller.DetailsUpdated += (_, u) => updates.Enqueue(u);

        poller.Start(client);
        await TestWait.UntilAsync(() => client.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "GetTorrentsAsync") >= 2);
        Assert.Empty(updates);

        poller.Watch("abc");
        await TestWait.UntilAsync(() => updates.Count >= 2);
        poller.Watch(null);
        var countAfterStop = updates.Count;
        await TestWait.UntilAsync(() => client.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "GetTorrentsAsync") >= 10);
        await poller.StopAsync();

        Assert.All(updates, u => Assert.Equal(new TorrentDetailsUpdate("abc", details), u));
        Assert.InRange(updates.Count, countAfterStop, countAfterStop + 1); // at most one poll was already in flight
    }

    [Fact]
    public async Task Watching_a_torrent_polls_at_once()
    {
        var client = ClientThatSucceeds();
        client.GetTorrentDetailsAsync("abc", Arg.Any<DetailFiles>(), Arg.Any<CancellationToken>()).Returns(TestData.MakeDetails("abc"));
        var updates = new ConcurrentQueue<TorrentDetailsUpdate>();
        using var poller = new TorrentPoller(TimeSpan.FromMinutes(10)); // only the first poll happens on its own
        poller.DetailsUpdated += (_, u) => updates.Enqueue(u);

        poller.Start(client);
        await TestWait.UntilAsync(() => client.ReceivedCalls().Any(c => c.GetMethodInfo().Name == "GetTorrentsAsync"));
        poller.Watch("abc");
        await TestWait.UntilAsync(() => !updates.IsEmpty);
        await poller.StopAsync();

        Assert.Equal("abc", updates.First().Hash);
    }

    [Fact]
    public async Task Files_are_requested_only_when_asked_for()
    {
        var client = ClientThatSucceeds();
        client.GetTorrentDetailsAsync("abc", Arg.Any<DetailFiles>(), Arg.Any<CancellationToken>()).Returns(TestData.MakeDetails("abc"));
        var updates = new ConcurrentQueue<TorrentDetailsUpdate>();
        using var poller = new TorrentPoller(TimeSpan.FromMilliseconds(5));
        poller.DetailsUpdated += (_, u) => updates.Enqueue(u);

        poller.Start(client);
        poller.Watch("abc");
        await TestWait.UntilAsync(() => updates.Count >= 1);
        poller.Watch("abc", includeFiles: true);
        await TestWait.UntilAsync(() => client.ReceivedCalls().Any(c =>
            c.GetMethodInfo().Name == "GetTorrentDetailsAsync" && (DetailFiles)c.GetArguments()[1]! != DetailFiles.None));
        await poller.StopAsync();

        await client.Received().GetTorrentDetailsAsync("abc", DetailFiles.None, Arg.Any<CancellationToken>());
    }

    private static TorrentFile MakeFile(int index, string path, long done) => new(index, path, 100, done, true, FilePriority.Normal);

    [Fact]
    public async Task File_names_are_fetched_once_then_only_progress_merged_into_them()
    {
        var client = ClientThatSucceeds();
        client.GetTorrentDetailsAsync("abc", DetailFiles.NamesAndProgress, Arg.Any<CancellationToken>())
            .Returns(TestData.MakeDetails("abc") with { Files = [MakeFile(0, "T/a.mkv", 10), MakeFile(1, "T/b.nfo", 5)] });
        client.GetTorrentDetailsAsync("abc", DetailFiles.ProgressOnly, Arg.Any<CancellationToken>())
            .Returns(TestData.MakeDetails("abc") with { FileProgress = [new(60, true, FilePriority.High), new(5, false, FilePriority.Normal)] });
        var updates = new ConcurrentQueue<TorrentDetailsUpdate>();
        using var poller = new TorrentPoller(TimeSpan.FromMilliseconds(5));
        poller.DetailsUpdated += (_, u) => updates.Enqueue(u);

        poller.Watch("abc", includeFiles: true);
        poller.Start(client);
        await TestWait.UntilAsync(() => updates.Count >= 4);
        await poller.StopAsync();

        await client.Received().GetTorrentDetailsAsync("abc", DetailFiles.ProgressOnly, Arg.Any<CancellationToken>());
        var later = updates.Last().Details!;
        Assert.Null(later.FileProgress);
        Assert.Equal(new[] { new TorrentFile(0, "T/a.mkv", 100, 60, true, FilePriority.High), new TorrentFile(1, "T/b.nfo", 100, 5, false, FilePriority.Normal) },
            later.Files);
        // Names only on the first poll and the full refresh (the first tick); later polls fetch progress only.
        Assert.Equal(1, client.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "GetTorrentDetailsAsync"
                                                          && (DetailFiles)c.GetArguments()[1]! == DetailFiles.NamesAndProgress));
    }

    [Fact]
    public async Task A_changed_number_of_files_fetches_the_names_again()
    {
        var client = ClientThatSucceeds();
        client.GetTorrentDetailsAsync("abc", DetailFiles.NamesAndProgress, Arg.Any<CancellationToken>())
            .Returns(TestData.MakeDetails("abc") with { Files = [MakeFile(0, "T/a.mkv", 10)] });
        client.GetTorrentDetailsAsync("abc", DetailFiles.ProgressOnly, Arg.Any<CancellationToken>())
            .Returns(TestData.MakeDetails("abc") with { FileProgress = [new(1, true, FilePriority.Normal), new(2, true, FilePriority.Normal)] });
        var updates = new ConcurrentQueue<TorrentDetailsUpdate>();
        using var poller = new TorrentPoller(TimeSpan.FromMilliseconds(5));
        poller.DetailsUpdated += (_, u) => updates.Enqueue(u);

        poller.Watch("abc", includeFiles: true);
        poller.Start(client);
        await TestWait.UntilAsync(() => client.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "GetTorrentDetailsAsync"
                                                                          && (DetailFiles)c.GetArguments()[1]! == DetailFiles.NamesAndProgress) >= 2);
        await poller.StopAsync();

        // The mismatched progress-only answer carries no files, so the Files tab keeps its last list.
        Assert.Contains(updates, u => u.Details!.Files is null && u.Details.FileProgress is null);
    }

    [Fact]
    public async Task An_action_makes_the_next_poll_fetch_the_file_names_again()
    {
        var client = ClientThatSucceeds();
        client.GetTorrentDetailsAsync("abc", Arg.Any<DetailFiles>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<DetailFiles>() == DetailFiles.NamesAndProgress
                ? TestData.MakeDetails("abc") with { Files = [MakeFile(0, "T/a.mkv", 10)] }
                : TestData.MakeDetails("abc") with { FileProgress = [new(1, true, FilePriority.Normal)] });
        using var poller = new TorrentPoller(TimeSpan.FromMilliseconds(5));
        int NameFetches() => client.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "GetTorrentDetailsAsync"
                                                               && (DetailFiles)c.GetArguments()[1]! == DetailFiles.NamesAndProgress);

        poller.Watch("abc", includeFiles: true);
        poller.Start(client);
        await TestWait.UntilAsync(() => client.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "GetTorrentDetailsAsync") >= 3);
        var before = NameFetches();
        poller.RequestRefresh(["abc"]); // e.g. after renaming a file
        await TestWait.UntilAsync(() => NameFetches() > before);
        await poller.StopAsync();
    }

    [Fact]
    public async Task A_targeted_refresh_reads_just_those_torrents_alongside_the_delta()
    {
        var client = ClientThatSucceeds();
        var stopped = TestData.MakeTorrent("a", 1) with { Status = TorrentStatus.Stopped };
        client.GetTorrentsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new TorrentUpdate([stopped], [], IsFullRefresh: false));
        var updates = new ConcurrentQueue<TorrentUpdate>();
        using var poller = new TorrentPoller(TimeSpan.FromMinutes(10)); // only the first poll and requested ones
        poller.TorrentsUpdated += (_, u) => updates.Enqueue(u);

        poller.Start(client);
        await TestWait.UntilAsync(() => updates.Count >= 1);
        poller.RequestRefresh(["a"]);
        await TestWait.UntilAsync(() => updates.Count >= 2);
        await poller.StopAsync();

        var second = updates.ElementAt(1);
        Assert.False(second.IsFullRefresh); // not the whole list
        Assert.Equal(stopped, Assert.Single(second.Torrents));
        await client.Received(1).GetTorrentsAsync(Arg.Is<IReadOnlyCollection<string>>(h => h.SequenceEqual(new[] { "a" })), Arg.Any<CancellationToken>());
        await client.Received(1).GetTorrentsAsync(true, Arg.Any<CancellationToken>()); // the delta, alongside
    }
}
