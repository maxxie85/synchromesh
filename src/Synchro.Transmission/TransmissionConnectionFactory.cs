using System.Net.Http.Headers;
using System.Text;
using Synchro.Core.Servers;
using Synchro.Transmission.JsonRpc;

namespace Synchro.Transmission;

public sealed class TransmissionConnectionFactory : ITransmissionConnectionFactory
{
    private readonly Func<HttpMessageHandler> _innerHandlerFactory;

    public TransmissionConnectionFactory()
        : this(CreateNetworkHandler)
    {
    }

    /// <summary>
    /// The real network layer. Asks for gzip: Transmission compresses RPC responses when the request says it accepts
    /// gzip, which makes a poll of a big server 5-10 times smaller.
    /// </summary>
    internal static SocketsHttpHandler CreateNetworkHandler() => new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = System.Net.DecompressionMethods.GZip,
    };

    // For tests: lets them substitute the network layer.
    internal TransmissionConnectionFactory(Func<HttpMessageHandler> innerHandlerFactory)
        => _innerHandlerFactory = innerHandlerFactory;

    public TransmissionConnection Create(ServerProfile profile, string? password)
    {
        var inner = _innerHandlerFactory();
        if (profile.UseHttps && !string.IsNullOrWhiteSpace(profile.ClientCertificatePath))
        {
            var certificate = ClientCertificateLoader.Load(profile.ClientCertificatePath, profile.ClientKeyPath);
            if (inner is SocketsHttpHandler sockets)
                sockets.SslOptions.ClientCertificates = [certificate];
        }

        var sessionHandler = new SessionIdHandler(inner);
        // The transport times each call itself: most get 20 seconds, a blocklist update longer.
        var http = new HttpClient(sessionHandler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };

        if (!string.IsNullOrEmpty(profile.Username))
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{profile.Username}:{password}"));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        return new TransmissionConnection(new JsonRpcTransport(http, profile.RpcUri), sessionHandler, http);
    }
}
