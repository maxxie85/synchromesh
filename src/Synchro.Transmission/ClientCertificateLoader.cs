using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Synchro.Transmission.JsonRpc;

namespace Synchro.Transmission;

/// <summary>Reads the client certificate a TLS proxy in front of Transmission may ask for.</summary>
internal static class ClientCertificateLoader
{
    /// <summary>
    /// A .p12/.pfx file (no password), or a PEM certificate with its PEM private key (in <paramref name="keyPath"/>, or
    /// in the certificate file itself). Throws <see cref="RpcClientCertificateException"/> with a message for the user.
    /// </summary>
    public static X509Certificate2 Load(string certificatePath, string? keyPath)
    {
        if (!File.Exists(certificatePath))
            throw new RpcClientCertificateException($"The client certificate file wasn't found: {certificatePath}");
        if (!string.IsNullOrWhiteSpace(keyPath) && !File.Exists(keyPath))
            throw new RpcClientCertificateException($"The private key file wasn't found: {keyPath}");

        try
        {
            var extension = Path.GetExtension(certificatePath).ToLowerInvariant();
            if (extension is ".p12" or ".pfx")
                return X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password: null);

            using var pem = string.IsNullOrWhiteSpace(keyPath)
                ? X509Certificate2.CreateFromPemFile(certificatePath)
                : X509Certificate2.CreateFromPemFile(certificatePath, keyPath);
            if (!pem.HasPrivateKey)
                throw new RpcClientCertificateException("The certificate has no private key. Choose the key file too.");

            // A PEM key only lives in memory; Windows' TLS stack can't use such a key, so make it a regular one.
            return X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), password: null);
        }
        catch (CryptographicException ex)
        {
            throw new RpcClientCertificateException(
                "Couldn't read the client certificate or its private key. Use PEM files with an unencrypted key, " +
                $"or a .p12/.pfx file without a password. ({ex.Message.TrimEnd('.')})", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RpcClientCertificateException($"Couldn't open the client certificate files: {ex.Message}", ex);
        }
        catch (ArgumentException ex)
        {
            // CreateFromPemFile: no certificate or no key found in the files.
            throw new RpcClientCertificateException(
                $"The files don't contain a PEM certificate and private key. ({ex.Message.TrimEnd('.')})", ex);
        }
    }
}
