using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Synchro.AppServices.Connection;
using Synchro.Core.Torrents;
using Synchro.ViewModels.Services;
using Xunit;

namespace Synchro.Tests;

public class ExternalAddQueueTests
{
    private readonly IAddTorrentFlow _flow = Substitute.For<IAddTorrentFlow>();
    private readonly IConnectionService _connection = Substitute.For<IConnectionService>();
    private static readonly AddTorrentSource[] BatchA = [new AddTorrentSource.Magnet("magnet:?xt=urn:btih:aaa", null)];
    private static readonly AddTorrentSource[] BatchB = [new AddTorrentSource.Magnet("magnet:?xt=urn:btih:bbb", null)];

    public ExternalAddQueueTests() => SetState(ConnectionState.Connected);

    private ExternalAddQueue Create() => new(_flow, _connection);

    private void SetState(ConnectionState state) =>
        _connection.Status.Returns(new ConnectionStatus(state));

    private void PublishStatus(ConnectionState state)
    {
        SetState(state);
        _connection.StatusChanged += Raise.Event();
    }

    [Fact]
    public async Task Nothing_is_delivered_before_start_up_finishes()
    {
        var queue = Create();

        queue.Enqueue(BatchA);

        await _flow.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public void Batches_received_during_start_up_are_delivered_in_order_on_start()
    {
        var order = new List<IReadOnlyList<AddTorrentSource>>();
        _flow.AddAsync(Arg.Do<IReadOnlyList<AddTorrentSource>>(b => order.Add(b)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var queue = Create();

        queue.Enqueue(BatchA);
        queue.Enqueue(BatchB);
        queue.Start();

        Assert.Equal(new IReadOnlyList<AddTorrentSource>[] { BatchA, BatchB }, order);
    }

    [Fact]
    public async Task After_start_a_batch_is_delivered_straight_away()
    {
        var queue = Create();
        queue.Start();

        queue.Enqueue(BatchA);

        await _flow.Received(1).AddAsync(BatchA, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task While_connecting_it_waits_and_delivers_once_connected()
    {
        SetState(ConnectionState.Connecting);
        var queue = Create();
        queue.Start();
        queue.Enqueue(BatchA);

        await _flow.DidNotReceiveWithAnyArgs().AddAsync(default!, default);

        PublishStatus(ConnectionState.Connected);

        await _flow.Received(1).AddAsync(BatchA, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Without_a_connection_the_flow_still_gets_the_batch_so_it_can_explain()
    {
        SetState(ConnectionState.Failed);
        var queue = Create();
        queue.Start();

        queue.Enqueue(BatchA);

        await _flow.Received(1).AddAsync(BatchA, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_batch_waits_until_the_previous_one_is_done()
    {
        var firstDone = new TaskCompletionSource();
        _flow.AddAsync(BatchA, Arg.Any<CancellationToken>()).Returns(firstDone.Task);
        var queue = Create();
        queue.Start();

        queue.Enqueue(BatchA);
        queue.Enqueue(BatchB);
        await _flow.DidNotReceive().AddAsync(BatchB, Arg.Any<CancellationToken>());

        firstDone.SetResult();
        await TestWait.UntilAsync(() => _flow.ReceivedCalls().Count() == 2);

        await _flow.Received(1).AddAsync(BatchB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failing_batch_does_not_block_the_next()
    {
        _flow.AddAsync(BatchA, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));
        var queue = Create();
        queue.Start();

        queue.Enqueue(BatchA);
        queue.Enqueue(BatchB);

        await _flow.Received(1).AddAsync(BatchB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Empty_batches_are_ignored()
    {
        var queue = Create();
        queue.Start();

        queue.Enqueue([]);

        await _flow.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }
}
