using NSubstitute;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Xunit;

namespace Synchro.Tests;

public class TorrentActionsTests
{
    private readonly IConnectionService _connection = Substitute.For<IConnectionService>();
    private readonly ITorrentPoller _poller = Substitute.For<ITorrentPoller>();
    private readonly ITransmissionClient _client = Substitute.For<ITransmissionClient>();

    private TorrentActions Create()
    {
        _connection.Client.Returns(_client);
        return new TorrentActions(_connection, _poller);
    }

    [Fact]
    public async Task Start_goes_to_the_client_and_then_refreshes_just_those_torrents()
    {
        await Create().StartAsync(["a", "b"], startNow: true);

        await _client.Received(1).StartAsync(Arg.Is<IReadOnlyList<string>>(h => h.SequenceEqual(new[] { "a", "b" })), true, Arg.Any<CancellationToken>());
        _poller.Received(1).RequestRefresh(Arg.Is<IReadOnlyCollection<string>>(h => h.SequenceEqual(new[] { "a", "b" })));
        _poller.DidNotReceive().RequestRefresh(); // not the whole list
    }

    [Fact]
    public async Task Stop_refreshes_its_torrent_while_remove_and_queue_moves_refresh_the_whole_list()
    {
        var actions = Create();

        await actions.StopAsync(["a"]);
        await actions.RemoveAsync(["a"], deleteLocalData: true);
        await actions.MoveInQueueAsync(["a"], QueueMove.Top);

        await _client.Received(1).StopAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await _client.Received(1).RemoveAsync(Arg.Any<IReadOnlyList<string>>(), true, Arg.Any<CancellationToken>());
        await _client.Received(1).MoveInQueueAsync(Arg.Any<IReadOnlyList<string>>(), QueueMove.Top, Arg.Any<CancellationToken>());
        _poller.Received(1).RequestRefresh(Arg.Is<IReadOnlyCollection<string>>(h => h.SequenceEqual(new[] { "a" })));
        _poller.Received(2).RequestRefresh(); // removed torrents and shifted queue positions can be anywhere
    }

    [Fact]
    public async Task Nothing_happens_for_an_empty_selection()
    {
        await Create().StopAsync([]);

        await _client.DidNotReceiveWithAnyArgs().StopAsync(default!, default);
        _poller.DidNotReceive().RequestRefresh();
        _poller.DidNotReceiveWithAnyArgs().RequestRefresh(default(IReadOnlyCollection<string>)!);
    }

    [Fact]
    public async Task Fails_clearly_when_not_connected()
    {
        _connection.Client.Returns((ITransmissionClient?)null);
        var actions = new TorrentActions(_connection, _poller);

        await Assert.ThrowsAsync<InvalidOperationException>(() => actions.StopAsync(["a"]));
    }

    [Fact]
    public async Task Does_not_refresh_when_the_client_fails()
    {
        _client.StopAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("boom")));

        await Assert.ThrowsAsync<HttpRequestException>(() => Create().StopAsync(["a"]));

        _poller.DidNotReceive().RequestRefresh();
        _poller.DidNotReceiveWithAnyArgs().RequestRefresh(default(IReadOnlyCollection<string>)!);
    }

    [Fact]
    public async Task Torrents_ending_up_with_the_same_labels_share_one_request()
    {
        var actions = Create();

        await actions.SetLabelsAsync(new Dictionary<string, IReadOnlyList<string>>
        {
            ["a"] = ["linux"], ["b"] = ["linux"], ["c"] = [],
        });

        await _client.Received(1).SetLabelsAsync(Arg.Is<IReadOnlyList<string>>(h => h.Count == 2), Arg.Is<IReadOnlyList<string>>(l => l.SequenceEqual(new[] { "linux" })), Arg.Any<CancellationToken>());
        await _client.Received(1).SetLabelsAsync(Arg.Is<IReadOnlyList<string>>(h => h.SequenceEqual(new[] { "c" })), Arg.Is<IReadOnlyList<string>>(l => l.Count == 0), Arg.Any<CancellationToken>());
        _poller.Received(1).RequestRefresh(Arg.Is<IReadOnlyCollection<string>>(h => h.Count == 3));
    }
}
