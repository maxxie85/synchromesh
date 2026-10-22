using NSubstitute;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Xunit;

namespace Synchro.Tests;

public class AddTorrentServiceTests
{
    private readonly IConnectionService _connection = Substitute.For<IConnectionService>();
    private readonly ITorrentPoller _poller = Substitute.For<ITorrentPoller>();
    private readonly ITransmissionClient _client = Substitute.For<ITransmissionClient>();
    private static readonly AddTorrentRequest Request = new(new AddTorrentSource.Magnet("magnet:?xt=urn:btih:abc", null), null, false);

    private AddTorrentService Create()
    {
        _connection.Client.Returns(_client);
        return new AddTorrentService(_connection, _poller);
    }

    [Fact]
    public async Task Adds_through_the_client_and_refreshes_the_list()
    {
        var expected = new AddTorrentResult(AddTorrentOutcome.Added, "abc", "Name");
        _client.AddTorrentAsync(Request, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await Create().AddAsync(Request);

        Assert.Equal(expected, result);
        _poller.Received(1).RequestRefresh(Arg.Is<IReadOnlyCollection<string>>(h => h.SequenceEqual(new[] { "abc" })));
        _poller.DidNotReceive().RequestRefresh();
    }

    [Fact]
    public async Task Does_not_refresh_when_the_server_rejects_the_torrent()
    {
        _client.AddTorrentAsync(Request, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AddTorrentResult>(new InvalidOperationException("invalid torrent")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Create().AddAsync(Request));

        _poller.DidNotReceiveWithAnyArgs().RequestRefresh(default(IReadOnlyCollection<string>)!);
    }

    [Fact]
    public async Task Passes_folder_queries_to_the_client()
    {
        _client.GetDefaultDownloadDirectoryAsync(Arg.Any<CancellationToken>()).Returns("/downloads");
        _client.GetFreeSpaceAsync("/downloads", Arg.Any<CancellationToken>()).Returns(123L);
        var service = Create();

        Assert.Equal("/downloads", await service.GetDefaultDownloadDirectoryAsync());
        Assert.Equal(123L, await service.GetFreeSpaceAsync("/downloads"));
    }

    [Fact]
    public async Task Fails_clearly_when_not_connected()
    {
        _connection.Client.Returns((ITransmissionClient?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new AddTorrentService(_connection, _poller).AddAsync(Request));
    }

    [Fact]
    public async Task Finishing_applies_only_non_default_choices_then_starts()
    {
        await Create().FinishAddAsync("abc", new FileChoices(Unwanted: [2], HighPriority: [0], LowPriority: []), start: true);

        Received.InOrder(() =>
        {
            _client.SetFilesWantedAsync("abc", Arg.Is<IReadOnlyList<int>>(i => i.SequenceEqual(new[] { 2 })), false, Arg.Any<CancellationToken>());
            _client.SetFilePriorityAsync("abc", Arg.Is<IReadOnlyList<int>>(i => i.SequenceEqual(new[] { 0 })), FilePriority.High, Arg.Any<CancellationToken>());
            _client.SetFilePriorityAsync("abc", Arg.Is<IReadOnlyList<int>>(i => i.Count == 0), FilePriority.Low, Arg.Any<CancellationToken>());
            _client.StartAsync(Arg.Is<IReadOnlyList<string>>(h => h.SequenceEqual(new[] { "abc" })), false, Arg.Any<CancellationToken>());
        });
        _poller.Received(1).RequestRefresh(Arg.Is<IReadOnlyCollection<string>>(h => h.SequenceEqual(new[] { "abc" })));
    }

    [Fact]
    public async Task Finishing_paused_stops_it_instead_of_starting()
    {
        await Create().FinishAddAsync("abc", null, start: false);

        await _client.DidNotReceiveWithAnyArgs().StartAsync(default!, default, default);
        await _client.Received(1).StopAsync(Arg.Is<IReadOnlyList<string>>(h => h.SequenceEqual(new[] { "abc" })), Arg.Any<CancellationToken>());
        await _client.DidNotReceiveWithAnyArgs().SetFilesWantedAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Holding_for_choice_stops_the_torrent()
    {
        await Create().HoldForChoiceAsync("abc");

        await _client.Received(1).StopAsync(Arg.Is<IReadOnlyList<string>>(h => h.SequenceEqual(new[] { "abc" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Undoing_removes_the_torrent_but_never_its_data()
    {
        await Create().UndoAddAsync("abc");

        await _client.Received(1).RemoveAsync(Arg.Is<IReadOnlyList<string>>(h => h.SequenceEqual(new[] { "abc" })), false, Arg.Any<CancellationToken>());
    }
}
