namespace Synchro.Transmission.JsonRpc;

/// <summary>Sends one JSON-RPC 2.0 call and returns its typed result, or throws an <see cref="RpcException"/>.</summary>
public interface IRpcTransport
{
    /// <summary>Calls with the usual timeout (20 seconds).</summary>
    Task<TResult> CallAsync<TParams, TResult>(string method, TParams? parameters, CancellationToken ct = default);

    /// <summary>For calls the server takes long to answer, such as downloading a blocklist.</summary>
    Task<TResult> CallAsync<TParams, TResult>(string method, TParams? parameters, TimeSpan timeout, CancellationToken ct = default);
}
