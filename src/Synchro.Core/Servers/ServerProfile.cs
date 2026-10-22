using System.Text.Json.Serialization;

namespace Synchro.Core.Servers;

/// <summary>
/// Everything needed to reach a Transmission server, except the password,
/// which lives in <see cref="ICredentialStore"/> so it never ends up in a JSON file.
/// </summary>
/// <param name="ClientCertificatePath">
/// For HTTPS behind a proxy that asks for a client certificate: a PEM certificate (with <paramref name="ClientKeyPath"/>,
/// or with the key in the same file), or a .p12/.pfx file without a password. Only the paths are stored.
/// </param>
public sealed record ServerProfile(
    Guid Id,
    string Name,
    string Host,
    int Port = 9091,
    bool UseHttps = false,
    string RpcPath = "/transmission/rpc",
    string? Username = null,
    string? ClientCertificatePath = null,
    string? ClientKeyPath = null)
{
    [JsonIgnore]
    public Uri RpcUri => new UriBuilder(UseHttps ? Uri.UriSchemeHttps : Uri.UriSchemeHttp, Host, Port, RpcPath).Uri;
}
