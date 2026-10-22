using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Synchro.Core.Servers;
using Synchro.Transmission;
using Xunit;

namespace Synchro.Tests;

public class CompressionTests
{
    [Fact]
    public void The_network_layer_asks_for_gzip()
    {
        using var handler = TransmissionConnectionFactory.CreateNetworkHandler();

        Assert.Equal(DecompressionMethods.GZip, handler.AutomaticDecompression);
    }

    [Theory]
    [InlineData(true)]  // Transmission compresses
    [InlineData(false)] // a server (or proxy) that doesn't: the same request still works
    public async Task Gzipped_and_plain_answers_are_both_read(bool compress)
    {
        // A tiny Transmission on localhost, answering after the usual 409 session handshake.
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        string? acceptEncoding = null;
        var server = Task.Run(async () =>
        {
            for (var i = 0; i < 2; i++)
            {
                var ctx = await listener.GetContextAsync();
                if (ctx.Request.Headers["X-Transmission-Session-Id"] is null)
                {
                    ctx.Response.StatusCode = 409;
                    ctx.Response.Headers["X-Transmission-Session-Id"] = "abc";
                    ctx.Response.Close();
                    continue;
                }
                acceptEncoding = ctx.Request.Headers["Accept-Encoding"];
                var body = Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"result":{"download_dir":"/data/completed"}}""");
                if (compress)
                {
                    using var zipped = new MemoryStream();
                    using (var gzip = new GZipStream(zipped, CompressionLevel.Fastest, leaveOpen: true)) gzip.Write(body);
                    body = zipped.ToArray();
                    ctx.Response.AddHeader("Content-Encoding", "gzip");
                }
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(body);
                ctx.Response.Close();
            }
        });

        var client = new TransmissionClientFactory(new TransmissionConnectionFactory())
            .Create(new ServerProfile(Guid.NewGuid(), "Local", "127.0.0.1", port), null, ServerCapabilities.Baseline);
        var folder = await client.GetDefaultDownloadDirectoryAsync();
        await server;

        Assert.Equal("/data/completed", folder);
        Assert.Contains("gzip", acceptEncoding);
    }

    private static int FreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }
}
