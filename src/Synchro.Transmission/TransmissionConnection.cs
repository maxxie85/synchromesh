using Synchro.Transmission.JsonRpc;

namespace Synchro.Transmission;

/// <summary>One configured HTTP pipeline to one server. Dispose it when done.</summary>
public sealed class TransmissionConnection : IDisposable
{
    private readonly HttpClient _http;
    private readonly SessionIdHandler _sessionHandler;

    internal TransmissionConnection(IRpcTransport transport, SessionIdHandler sessionHandler, HttpClient http)
    {
        Transport = transport;
        _sessionHandler = sessionHandler;
        _http = http;
    }

    public IRpcTransport Transport { get; }

    /// <summary>Value of X-Transmission-Rpc-Version seen so far, if the server sent it.</summary>
    public string? ServerRpcVersion => _sessionHandler.ServerRpcVersion;

    public void Dispose() => _http.Dispose();
}
