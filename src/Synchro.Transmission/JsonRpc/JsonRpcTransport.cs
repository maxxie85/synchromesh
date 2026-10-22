using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Synchro.Transmission.JsonRpc;

/// <summary>
/// JSON-RPC over HTTP POST. Timeouts are per call, so <paramref name="http"/> should have none of its own
/// (<see cref="Timeout.InfiniteTimeSpan"/>).
/// </summary>
internal sealed class JsonRpcTransport(HttpClient http, Uri endpoint) : IRpcTransport
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    /// <summary>JSON-RPC 2.0's error code for an unknown method.</summary>
    internal const int MethodNotFound = -32601;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private long _nextId;

    public Task<TResult> CallAsync<TParams, TResult>(string method, TParams? parameters, CancellationToken ct = default)
        => CallAsync<TParams, TResult>(method, parameters, DefaultTimeout, ct);

    public async Task<TResult> CallAsync<TParams, TResult>(string method, TParams? parameters, TimeSpan timeout, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            return await SendAsync<TParams, TResult>(method, parameters, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new RpcUnreachableException("The connection timed out.");
        }
    }

    private async Task<TResult> SendAsync<TParams, TResult>(string method, TParams? parameters, CancellationToken ct)
    {
        var request = new RpcRequest<TParams>("2.0", method, parameters, Interlocked.Increment(ref _nextId));
        using var content = JsonContent.Create(request, mediaType: null, Options);

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(endpoint, content, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new RpcUnreachableException(ex.InnerException?.Message ?? ex.Message, ex);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                    throw new RpcAuthenticationException();
                case HttpStatusCode.MisdirectedRequest: // host whitelist (rpc-host-whitelist)
                case HttpStatusCode.Forbidden:          // IP whitelist (rpc-whitelist)
                    throw new RpcHostRejectedException((int)response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
                throw new RpcProtocolException($"Unexpected HTTP status {(int)response.StatusCode}.");

            RpcResponse<TResult>? rpc;
            try
            {
                rpc = await response.Content.ReadFromJsonAsync<RpcResponse<TResult>>(Options, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                // NotSupportedException: the server answered with a non-JSON content type (e.g. an HTML login page).
                throw new RpcProtocolException("The response was not valid JSON-RPC.", ex);
            }

            if (rpc is null || rpc.JsonRpc != "2.0")
                throw new RpcProtocolException("The response was not a JSON-RPC 2.0 message.");

            if (rpc.Error is { } error)
            {
                // JSON-RPC "method not found": most likely a Transmission version that dropped or renamed it.
                if (error.Code == MethodNotFound)
                    throw new RpcServerException(error.Code,
                        $"The server doesn't know the request \"{method}\". It may run a Transmission version this app doesn't support",
                        null);
                throw new RpcServerException(error.Code, error.Message ?? "RPC error", error.Data?.ErrorString);
            }

            if (rpc.Result is not null)
                return rpc.Result;

            // Action methods (torrent_stop, queue_move_top, ...) have no response parameters.
            if (typeof(TResult) == typeof(EmptyResult))
                return (TResult)(object)new EmptyResult();

            throw new RpcProtocolException("The response had no result.");
        }
    }
}
