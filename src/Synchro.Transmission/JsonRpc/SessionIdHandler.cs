using System.Net;

namespace Synchro.Transmission.JsonRpc;

/// <summary>
/// Implements Transmission's CSRF handshake (spec 2.2.1): a 409 response carries the
/// X-Transmission-Session-Id to use; we store it and resend the same request.
/// Also remembers X-Transmission-Rpc-Version from those 409 responses (sent by 4.1.0+).
/// </summary>
internal sealed class SessionIdHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public const string SessionIdHeader = "X-Transmission-Session-Id";
    public const string RpcVersionHeader = "X-Transmission-Rpc-Version";
    private const int MaxRetries = 2;

    private volatile string? _sessionId;
    private volatile string? _serverRpcVersion;

    public string? ServerRpcVersion => _serverRpcVersion;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        // An HttpRequestMessage can only be sent once, so keep the body to build copies for retries.
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

        var attempt = request;
        for (var retry = 0; ; retry++)
        {
            ApplySessionId(attempt);
            var response = await base.SendAsync(attempt, ct).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.Conflict || retry >= MaxRetries)
                return response;

            if (!CaptureHeaders(response))
                return response; // A 409 without a session id isn't the CSRF handshake.

            response.Dispose();
            attempt = Clone(request, body);
        }
    }

    private void ApplySessionId(HttpRequestMessage request)
    {
        request.Headers.Remove(SessionIdHeader);
        if (_sessionId is { } id)
            request.Headers.TryAddWithoutValidation(SessionIdHeader, id);
    }

    private bool CaptureHeaders(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues(RpcVersionHeader, out var versions))
            _serverRpcVersion = versions.FirstOrDefault();

        if (response.Headers.TryGetValues(SessionIdHeader, out var ids) && ids.FirstOrDefault() is { Length: > 0 } id)
        {
            _sessionId = id;
            return true;
        }

        return false;
    }

    private static HttpRequestMessage Clone(HttpRequestMessage original, byte[]? body)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri) { Version = original.Version };

        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            foreach (var header in original.Content!.Headers)
            {
                if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
