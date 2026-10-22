using System.Text.Json.Serialization;

namespace Synchro.Transmission.JsonRpc;

internal sealed record RpcRequest<TParams>(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    string Method,
    TParams? Params,
    long Id);

internal sealed class RpcResponse<TResult>
{
    [JsonPropertyName("jsonrpc")] public string? JsonRpc { get; init; }
    public TResult? Result { get; init; }
    public RpcError? Error { get; init; }
    public long? Id { get; init; }
}

internal sealed class RpcError
{
    public int Code { get; init; }
    public string? Message { get; init; }
    public RpcErrorData? Data { get; init; }
}

internal sealed class RpcErrorData
{
    public string? ErrorString { get; init; }
}

/// <summary>For methods whose result object we don't care about.</summary>
internal sealed class EmptyResult;
