using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Synchro.Core.Servers;
using Synchro.Transmission;
using Synchro.Transmission.JsonRpc;
using Xunit;

namespace Synchro.Tests;

public sealed class ClientCertificateTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tm-certs").FullName;
    private readonly string _certPem;
    private readonly string _keyPem;
    private readonly X509Certificate2 _certificate;

    public ClientCertificateTests()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=transmission-remote-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        _certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        _certPem = _certificate.ExportCertificatePem();
        _keyPem = key.ExportPkcs8PrivateKeyPem();
    }

    public void Dispose()
    {
        _certificate.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void A_pem_certificate_and_key_load_with_the_private_key()
    {
        using var loaded = ClientCertificateLoader.Load(Write("client.pem", _certPem), Write("client.key", _keyPem));

        Assert.True(loaded.HasPrivateKey);
        Assert.Equal(_certificate.Thumbprint, loaded.Thumbprint);
    }

    [Fact]
    public void The_key_may_be_in_the_certificate_file()
    {
        using var loaded = ClientCertificateLoader.Load(Write("both.pem", _certPem + "\n" + _keyPem), null);

        Assert.True(loaded.HasPrivateKey);
    }

    [Fact]
    public void A_p12_file_without_a_password_loads()
    {
        var path = Path.Combine(_dir, "client.p12");
        using (var withKey = X509Certificate2.CreateFromPem(_certPem, _keyPem))
            File.WriteAllBytes(path, withKey.Export(X509ContentType.Pkcs12));

        using var loaded = ClientCertificateLoader.Load(path, null);

        Assert.True(loaded.HasPrivateKey);
    }

    [Fact]
    public void A_missing_file_is_named()
    {
        var path = Path.Combine(_dir, "missing.pem");

        var ex = Assert.Throws<RpcClientCertificateException>(() => ClientCertificateLoader.Load(path, null));

        Assert.Contains("wasn't found", ex.Message);
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void A_certificate_without_its_key_says_to_choose_the_key()
    {
        var ex = Assert.Throws<RpcClientCertificateException>(() => ClientCertificateLoader.Load(Write("only.pem", _certPem), null));

        Assert.Contains("private key", ex.Message);
    }

    [Fact]
    public void An_encrypted_key_is_explained()
    {
        using var key = RSA.Create(2048);
        var encrypted = key.ExportEncryptedPkcs8PrivateKeyPem("secret",
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 10));

        var ex = Assert.Throws<RpcClientCertificateException>(() =>
            ClientCertificateLoader.Load(Write("client.pem", _certPem), Write("enc.key", encrypted)));

        Assert.Contains("unencrypted key", ex.Message);
    }

    [Fact]
    public async Task The_connection_test_reports_a_certificate_problem_without_connecting()
    {
        var fake = new FakeHttpHandler(_ => throw new InvalidOperationException("no request expected"));
        var tester = new ConnectionTester(new TransmissionConnectionFactory(() => fake));
        var profile = new ServerProfile(Guid.NewGuid(), "NAS", "nas", 443, UseHttps: true,
            ClientCertificatePath: Path.Combine(_dir, "missing.pem"));

        var result = await tester.TestAsync(profile, null);

        var failure = Assert.IsType<ConnectionTestResult.Failure>(result);
        Assert.Equal(ConnectionFailure.ClientCertificate, failure.Reason);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task Without_https_the_certificate_is_not_used()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(FakeHttpHandler.SessionGetJson()));
        var tester = new ConnectionTester(new TransmissionConnectionFactory(() => fake));
        var profile = new ServerProfile(Guid.NewGuid(), "NAS", "nas", ClientCertificatePath: Path.Combine(_dir, "missing.pem"));

        Assert.IsType<ConnectionTestResult.Success>(await tester.TestAsync(profile, null));
    }
}
