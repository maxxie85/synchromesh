namespace Synchro.Transmission.JsonRpc;

public abstract class RpcException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Network failure, refused connection or timeout.</summary>
public sealed class RpcUnreachableException(string message, Exception? inner = null) : RpcException(message, inner);

/// <summary>The client certificate or its private key couldn't be read.</summary>
public sealed class RpcClientCertificateException(string message, Exception? inner = null) : RpcException(message, inner);

/// <summary>HTTP 401.</summary>
public sealed class RpcAuthenticationException() : RpcException("The server rejected the credentials.");

/// <summary>HTTP 421 / 403: host whitelist or IP whitelist refused the request.</summary>
public sealed class RpcHostRejectedException(int statusCode) : RpcException($"The server refused the request (HTTP {statusCode}).");

/// <summary>The endpoint answered, but not with a valid JSON-RPC 2.0 response.</summary>
public sealed class RpcProtocolException(string message, Exception? inner = null) : RpcException(message, inner);

/// <summary>A well-formed JSON-RPC error object returned by Transmission.</summary>
public sealed class RpcServerException(int code, string message, string? errorString)
    : RpcException(errorString is null ? message : $"{message}: {errorString}")
{
    public int Code { get; } = code;
}
