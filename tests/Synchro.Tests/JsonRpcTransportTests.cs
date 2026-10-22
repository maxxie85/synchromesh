using System.Net;
using Synchro.Transmission.JsonRpc;
using Xunit;

namespace Synchro.Tests;

public class JsonRpcTransportTests
{
    private sealed record Params(string[] Fields);
    private sealed class Result { public string? Version { get; init; } public string? RpcVersionSemver { get; init; } }

    private static JsonRpcTransport Create(FakeHttpHandler fake) =>
        new(new HttpClient(fake), new Uri("http://nas:9091/transmission/rpc"));

    [Fact]
    public async Task Sends_a_snake_case_jsonrpc_2_request_and_parses_the_result()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(FakeHttpHandler.SessionGetJson()));

        var result = await Create(fake).CallAsync<Params, Result>("session_get", new Params(["version"]));

        Assert.Equal("4.1.0 (abc)", result.Version);
        Assert.Equal("6.0.0", result.RpcVersionSemver);
        var body = fake.Calls.Single().Body!;
        Assert.Contains("\"jsonrpc\":\"2.0\"", body);
        Assert.Contains("\"method\":\"session_get\"", body);
        Assert.Contains("\"params\":{\"fields\":[\"version\"]}", body);
    }

    [Fact]
    public async Task Maps_http_401_to_an_authentication_error()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<RpcAuthenticationException>(() => Create(fake).CallAsync<Params, Result>("session_get", null));
    }

    [Theory]
    [InlineData(HttpStatusCode.MisdirectedRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Maps_whitelist_refusals_to_host_rejected(HttpStatusCode status)
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(status));
        await Assert.ThrowsAsync<RpcHostRejectedException>(() => Create(fake).CallAsync<Params, Result>("session_get", null));
    }

    [Fact]
    public async Task Surfaces_a_jsonrpc_error_object()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(
            """{"jsonrpc":"2.0","error":{"code":7,"message":"HTTP error from backend service","data":{"error_string":"No Response"}},"id":1}"""));

        var ex = await Assert.ThrowsAsync<RpcServerException>(() => Create(fake).CallAsync<Params, Result>("port_test", null));

        Assert.Equal(7, ex.Code);
        Assert.Contains("No Response", ex.Message);
    }

    [Fact]
    public async Task Rejects_a_non_jsonrpc_body_as_a_protocol_error()
    {
        // What a pre-4.1 Transmission answers to an unknown method.
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"arguments":{},"result":"no method name"}"""));
        await Assert.ThrowsAsync<RpcProtocolException>(() => Create(fake).CallAsync<Params, Result>("session_get", null));
    }

    [Fact]
    public async Task Maps_network_errors_to_unreachable()
    {
        var fake = new FakeHttpHandler(_ => throw new HttpRequestException("Connection refused"));
        await Assert.ThrowsAsync<RpcUnreachableException>(() => Create(fake).CallAsync<Params, Result>("session_get", null));
    }

    [Fact]
    public async Task An_unknown_method_names_the_request_and_hints_at_the_version()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(
            """{"jsonrpc":"2.0","error":{"code":-32601,"message":"Method not found"},"id":1}"""));

        var ex = await Assert.ThrowsAsync<RpcServerException>(() => Create(fake).CallAsync<Params, Result>("torrent_get", null));

        Assert.Equal(-32601, ex.Code);
        Assert.Contains("\"torrent_get\"", ex.Message);
        Assert.Contains("Transmission version", ex.Message);
    }
}
