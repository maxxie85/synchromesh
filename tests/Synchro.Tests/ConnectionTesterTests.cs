using System.Net;
using Synchro.Core.Servers;
using Synchro.Transmission;
using Xunit;

namespace Synchro.Tests;

public class ConnectionTesterTests
{
    private static readonly ServerProfile Profile = new(Guid.NewGuid(), "NAS", "nas.local", Username: "admin");

    private static ConnectionTester Create(FakeHttpHandler fake) =>
        new(new TransmissionConnectionFactory(() => fake));

    [Fact]
    public async Task Succeeds_after_the_session_id_handshake_and_trims_the_revision()
    {
        var fake = new FakeHttpHandler(call => call.SessionId is null
            ? FakeHttpHandler.Conflict("abc", "6.1.0")
            : FakeHttpHandler.Json(FakeHttpHandler.SessionGetJson("4.2.0 (deadbeef)", "6.1.0")));

        var result = await Create(fake).TestAsync(Profile, "secret");

        var success = Assert.IsType<ConnectionTestResult.Success>(result);
        Assert.Equal("4.2.0", success.ServerVersion);
        Assert.Equal(new Version(6, 1, 0), success.Capabilities.Rpc);
        Assert.StartsWith("Basic ", fake.Calls[0].Authorization);
    }

    [Fact]
    public async Task Reports_a_server_that_is_too_old()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(FakeHttpHandler.SessionGetJson("4.0.6", "5.3.0")));

        var result = await Create(fake).TestAsync(Profile, null);

        Assert.Equal(ConnectionFailure.ServerTooOld, Assert.IsType<ConnectionTestResult.Failure>(result).Reason);
    }

    [Fact]
    public async Task Reports_old_servers_that_announce_their_version_in_the_409()
    {
        var fake = new FakeHttpHandler(call => call.SessionId is null
            ? FakeHttpHandler.Conflict("abc", "5.3.0")
            : FakeHttpHandler.Json("""{"arguments":{},"result":"no method name"}"""));

        var result = await Create(fake).TestAsync(Profile, null);

        Assert.Equal(ConnectionFailure.ServerTooOld, Assert.IsType<ConnectionTestResult.Failure>(result).Reason);
    }

    [Fact]
    public async Task Reports_bad_credentials()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await Create(fake).TestAsync(Profile, "wrong");

        Assert.Equal(ConnectionFailure.AuthenticationFailed, Assert.IsType<ConnectionTestResult.Failure>(result).Reason);
    }

    [Fact]
    public async Task Reports_an_unreachable_host()
    {
        var fake = new FakeHttpHandler(_ => throw new HttpRequestException("Connection refused"));

        var result = await Create(fake).TestAsync(Profile, null);

        var failure = Assert.IsType<ConnectionTestResult.Failure>(result);
        Assert.Equal(ConnectionFailure.Unreachable, failure.Reason);
        Assert.Contains("nas.local", failure.Message);
    }

    [Fact]
    public async Task Reports_a_non_transmission_endpoint()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>router login</html>", System.Text.Encoding.UTF8, "text/html"),
        });

        var result = await Create(fake).TestAsync(Profile, null);

        Assert.Equal(ConnectionFailure.NotTransmission, Assert.IsType<ConnectionTestResult.Failure>(result).Reason);
    }

    [Fact]
    public async Task Connects_to_a_newer_major_version_but_marks_it_untested()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(FakeHttpHandler.SessionGetJson("5.0.0", "7.0.0")));

        var result = await Create(fake).TestAsync(Profile, null);

        Assert.True(Assert.IsType<ConnectionTestResult.Success>(result).Capabilities.IsUntested);
    }
}
