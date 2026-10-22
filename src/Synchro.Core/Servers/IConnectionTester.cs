namespace Synchro.Core.Servers;

public interface IConnectionTester
{
    /// <summary>
    /// Performs the full handshake (session id, authentication, version check) against a profile
    /// that does not have to be the active one. Never throws for expected failures.
    /// </summary>
    Task<ConnectionTestResult> TestAsync(ServerProfile profile, string? password, CancellationToken ct = default);
}

public enum ConnectionFailure
{
    Unreachable,
    AuthenticationFailed,
    HostRejected,
    NotTransmission,
    ServerTooOld,
    ClientCertificate,
    Unknown,
}

public abstract record ConnectionTestResult
{
    private ConnectionTestResult() { }

    /// <param name="ServerVersion">Transmission's own version for display ("4.1.3").</param>
    /// <param name="Capabilities">From the RPC version: what the client may use, and whether to warn.</param>
    public sealed record Success(string ServerVersion, ServerCapabilities Capabilities) : ConnectionTestResult;

    public sealed record Failure(ConnectionFailure Reason, string Message) : ConnectionTestResult;
}
