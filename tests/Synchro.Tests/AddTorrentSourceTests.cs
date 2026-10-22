using Synchro.Core.Torrents;
using Xunit;

namespace Synchro.Tests;

public class AddTorrentSourceTests
{
    [Fact]
    public void Recognises_a_magnet_link_and_reads_its_display_name()
    {
        var ok = AddTorrentSource.TryParseLink(
            "magnet:?xt=urn:btih:c12fe1c06bba254a9dc9f519b335aa7c1367a88a&dn=Ubuntu+26.04%20Desktop&tr=udp%3A%2F%2Fx", out var source);

        Assert.True(ok);
        var magnet = Assert.IsType<AddTorrentSource.Magnet>(source);
        Assert.Equal("Ubuntu 26.04 Desktop", magnet.DisplayName);
    }

    [Fact]
    public void A_magnet_link_without_a_name_is_still_valid()
    {
        Assert.True(AddTorrentSource.TryParseLink("magnet:?xt=urn:btih:abc", out var source));
        Assert.Null(Assert.IsType<AddTorrentSource.Magnet>(source).DisplayName);
    }

    [Fact]
    public void The_info_hash_may_come_after_other_parameters()
    {
        Assert.True(AddTorrentSource.TryParseLink("magnet:?dn=Name&xt=urn:btih:abc", out _));
    }

    [Fact]
    public void Surrounding_whitespace_is_ignored()
    {
        Assert.True(AddTorrentSource.TryParseLink("  magnet:?xt=urn:btih:abc \n", out var source));
        Assert.Equal("magnet:?xt=urn:btih:abc", Assert.IsType<AddTorrentSource.Magnet>(source).Link);
    }

    [Theory]
    [InlineData("magnet:?dn=NoHashHere")]
    [InlineData("magnet:")]
    [InlineData("hello world")]
    [InlineData("ftp://example.org/a.torrent")]
    [InlineData("file:///tmp/a.torrent")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rejects_anything_else(string? text)
        => Assert.False(AddTorrentSource.TryParseLink(text, out _));

    [Theory]
    [InlineData("http://example.org/a.torrent")]
    [InlineData("https://example.org/download?id=7")]
    public void Recognises_http_and_https_links(string text)
    {
        Assert.True(AddTorrentSource.TryParseLink(text, out var source));
        Assert.IsType<AddTorrentSource.Url>(source);
    }
}
