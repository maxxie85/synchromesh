using Synchro.Transmission.JsonRpc;
using Xunit;

namespace Synchro.Tests;

public class JsonRpcTransportTimeoutTests
{
    // Never answers: only cancellation ends the request.
    private sealed class SilentHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        }
    }

    private static JsonRpcTransport Create() =>
        new(new HttpClient(new SilentHandler()) { Timeout = Timeout.InfiniteTimeSpan }, new Uri("http://nas:9091/transmission/rpc"));

    [Fact]
    public async Task A_call_that_takes_too_long_is_reported_as_timed_out()
    {
        var ex = await Assert.ThrowsAsync<RpcUnreachableException>(() =>
            Create().CallAsync<object, EmptyResult>("blocklist_update", null, TimeSpan.FromMilliseconds(50)));

        Assert.Contains("timed out", ex.Message);
    }

    [Fact]
    public async Task Cancelling_is_not_reported_as_a_timeout()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Create().CallAsync<object, EmptyResult>("session_get", null, TimeSpan.FromMinutes(5), cts.Token));
    }
}
