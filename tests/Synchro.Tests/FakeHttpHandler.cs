using System.Net;
using System.Text;

namespace Synchro.Tests;

internal sealed record RecordedCall(string? SessionId, string? Authorization, string? Body);

/// <summary>Stands in for the network: records requests and answers with whatever the test supplies.</summary>
internal sealed class FakeHttpHandler(Func<RecordedCall, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<RecordedCall> Calls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        var call = new RecordedCall(
            request.Headers.TryGetValues("X-Transmission-Session-Id", out var ids) ? ids.First() : null,
            request.Headers.Authorization?.ToString(),
            body);

        Calls.Add(call);
        return respond(call);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Conflict(string sessionId, string? rpcVersion = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Conflict);
        response.Headers.Add("X-Transmission-Session-Id", sessionId);
        if (rpcVersion is not null) response.Headers.Add("X-Transmission-Rpc-Version", rpcVersion);
        return response;
    }

    public static string SessionGetJson(string version = "4.1.0 (abc)", string rpc = "6.0.0") =>
        $$"""{"jsonrpc":"2.0","result":{"version":"{{version}}","rpc_version_semver":"{{rpc}}"},"id":1}""";
}
