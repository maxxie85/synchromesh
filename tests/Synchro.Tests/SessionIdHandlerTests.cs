using System.Net;
using System.Text;
using Synchro.Transmission.JsonRpc;
using Xunit;

namespace Synchro.Tests;

public class SessionIdHandlerTests
{
    [Fact]
    public async Task Retries_once_with_the_session_id_from_a_409_and_resends_the_body()
    {
        var fake = new FakeHttpHandler(call => call.SessionId is null
            ? FakeHttpHandler.Conflict("abc", "6.0.0")
            : FakeHttpHandler.Json("{}"));
        var handler = new SessionIdHandler(fake);
        using var http = new HttpClient(handler);

        var response = await http.PostAsync("http://nas/rpc", new StringContent("""{"a":1}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, fake.Calls.Count);
        Assert.Null(fake.Calls[0].SessionId);
        Assert.Equal("abc", fake.Calls[1].SessionId);
        Assert.Equal("""{"a":1}""", fake.Calls[1].Body);
        Assert.Equal("6.0.0", handler.ServerRpcVersion);
    }

    [Fact]
    public async Task Reuses_the_session_id_on_later_requests()
    {
        var fake = new FakeHttpHandler(call => call.SessionId is null
            ? FakeHttpHandler.Conflict("abc")
            : FakeHttpHandler.Json("{}"));
        using var http = new HttpClient(new SessionIdHandler(fake));

        await http.PostAsync("http://nas/rpc", new StringContent("{}"));
        await http.PostAsync("http://nas/rpc", new StringContent("{}"));

        Assert.Equal(3, fake.Calls.Count); // 409, retry, then one clean request
        Assert.Equal("abc", fake.Calls[2].SessionId);
    }

    [Fact]
    public async Task Returns_a_409_without_a_session_id_untouched()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict));
        using var http = new HttpClient(new SessionIdHandler(fake));

        var response = await http.PostAsync("http://nas/rpc", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(fake.Calls);
    }
}
