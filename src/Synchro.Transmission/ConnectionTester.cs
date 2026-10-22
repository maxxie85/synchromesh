using Synchro.Core.Servers;
using Synchro.Transmission.JsonRpc;

namespace Synchro.Transmission;

public sealed class ConnectionTester(ITransmissionConnectionFactory factory) : IConnectionTester
{
    private sealed record SessionGetParams(string[] Fields);

    private sealed class SessionGetResult
    {
        public string? Version { get; init; }
        public string? RpcVersionSemver { get; init; }
    }

    public async Task<ConnectionTestResult> TestAsync(ServerProfile profile, string? password, CancellationToken ct = default)
    {
        TransmissionConnection connection;
        try
        {
            connection = factory.Create(profile, password);
        }
        catch (RpcClientCertificateException ex)
        {
            return Fail(ConnectionFailure.ClientCertificate, ex.Message);
        }

        using var _ = connection;
        try
        {
            var session = await connection.Transport
                .CallAsync<SessionGetParams, SessionGetResult>(
                    "session_get", new SessionGetParams(["version", "rpc_version_semver"]), ct)
                .ConfigureAwait(false);

            if (!RpcVersion.IsSupported(session.RpcVersionSemver) || !RpcVersion.TryParse(session.RpcVersionSemver, out var rpc))
                return TooOld(session.RpcVersionSemver);

            // "4.1.0-dev (ae226418eb)" -> "4.1.0-dev"
            var version = session.Version?.Split(' ')[0] ?? "(unknown version)";
            return new ConnectionTestResult.Success(version, new ServerCapabilities(rpc));
        }
        catch (RpcUnreachableException ex)
        {
            return Fail(ConnectionFailure.Unreachable,
                $"Couldn't connect to {profile.Host} on port {profile.Port}. {ex.Message} " +
                "Check the address and port, and that Transmission is running.");
        }
        catch (RpcAuthenticationException)
        {
            return Fail(ConnectionFailure.AuthenticationFailed,
                "The server rejected the username or password.");
        }
        catch (RpcHostRejectedException)
        {
            return Fail(ConnectionFailure.HostRejected,
                "The server refused this request. In the server's settings, add this address to " +
                "rpc-host-whitelist, and check that your IP is allowed by rpc-whitelist.");
        }
        catch (RpcProtocolException)
        {
            // Servers older than 4.1.0 don't send the version header, so we can only hint.
            if (connection.ServerRpcVersion is { } v && !RpcVersion.IsSupported(v))
                return TooOld(v);

            return Fail(ConnectionFailure.NotTransmission,
                "The address answered, but not as Transmission 4.1 or newer. Check the port and RPC path " +
                "(usually /transmission/rpc). Older Transmission versions aren't supported.");
        }
        catch (RpcException ex)
        {
            return Fail(ConnectionFailure.Unknown, ex.Message);
        }
    }

    private static ConnectionTestResult TooOld(string? rpcVersion) =>
        Fail(ConnectionFailure.ServerTooOld,
            $"This server uses RPC version {rpcVersion ?? "unknown"}. " +
            "Transmission 4.1.0 (RPC 6.0.0) or newer is required.");

    private static ConnectionTestResult Fail(ConnectionFailure reason, string message) =>
        new ConnectionTestResult.Failure(reason, message);
}
