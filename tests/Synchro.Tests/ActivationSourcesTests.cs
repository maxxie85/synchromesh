using Synchro.Core.Torrents;
using Synchro.Desktop.Services;
using Xunit;

namespace Synchro.Tests;

public sealed class ActivationSourcesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tm-activation-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteFile(string name, byte[] content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public async Task Arguments_can_be_magnet_links_urls_and_torrent_files()
    {
        var path = WriteFile("ubuntu.torrent", [1, 2, 3]);

        var sources = await ActivationSources.FromArgumentsAsync(
            ["magnet:?xt=urn:btih:aaa&dn=Ubuntu", "https://example.org/a.torrent", path]);

        Assert.Collection(sources,
            s => Assert.Equal("Ubuntu", Assert.IsType<AddTorrentSource.Magnet>(s).DisplayName),
            s => Assert.Equal("https://example.org/a.torrent", Assert.IsType<AddTorrentSource.Url>(s).Address.ToString()),
            s =>
            {
                var file = Assert.IsType<AddTorrentSource.TorrentFile>(s);
                Assert.Equal("ubuntu.torrent", file.FileName);
                Assert.Equal(new byte[] { 1, 2, 3 }, file.Content);
            });
    }

    [Fact]
    public async Task Unrelated_arguments_and_missing_or_other_files_are_skipped()
    {
        var notATorrent = WriteFile("notes.txt", [1]);

        var sources = await ActivationSources.FromArgumentsAsync(
            ["-psn_0_12345", "--verbose", notATorrent, Path.Combine(_dir, "missing.torrent")]);

        Assert.Empty(sources);
    }

    [Fact]
    public async Task A_file_uri_argument_is_read_like_a_path()
    {
        var path = WriteFile("with space.torrent", [7]);

        var sources = await ActivationSources.FromArgumentsAsync([new Uri(path).AbsoluteUri]); // file:///…/with%20space.torrent

        Assert.Equal("with space.torrent", Assert.IsType<AddTorrentSource.TorrentFile>(Assert.Single(sources)).FileName);
    }

    [Fact]
    public async Task A_magnet_uri_becomes_a_magnet_source()
    {
        var sources = await ActivationSources.FromUriAsync(new Uri("magnet:?xt=urn:btih:aaa&dn=Some+Name"));

        var magnet = Assert.IsType<AddTorrentSource.Magnet>(Assert.Single(sources));
        Assert.Equal("magnet:?xt=urn:btih:aaa&dn=Some+Name", magnet.Link);
        Assert.Equal("Some Name", magnet.DisplayName);
    }

    [Fact]
    public async Task A_file_uri_is_read_as_a_torrent_file()
    {
        var path = WriteFile("with space.torrent", [9]);

        var sources = await ActivationSources.FromUriAsync(new Uri(path));

        Assert.Equal("with space.torrent", Assert.IsType<AddTorrentSource.TorrentFile>(Assert.Single(sources)).FileName);
    }

    [Fact]
    public async Task An_unknown_scheme_gives_nothing()
    {
        Assert.Empty(await ActivationSources.FromUriAsync(new Uri("mailto:someone@example.org")));
    }
}
