using System.Text;
using Synchro.Core.Metainfo;
using Xunit;
using static Synchro.Tests.TorrentBuilder;

namespace Synchro.Tests;

public class TorrentMetainfoTests
{
    [Fact]
    public void A_multi_file_torrent_lists_files_in_torrent_order_with_the_name_as_root()
    {
        var torrent = MultiFile("Mayday", (["b.nfo"], 947), (["Screens", "s1.jpg"], 210_000), (["a.mkv"], 8_280_000_000));

        Assert.True(TorrentMetainfo.TryParse(torrent, out var info));

        Assert.Equal("Mayday", info!.Name);
        Assert.Equal(new[]
        {
            new MetainfoFile(0, "Mayday/b.nfo", 947, false),
            new MetainfoFile(1, "Mayday/Screens/s1.jpg", 210_000, false),
            new MetainfoFile(2, "Mayday/a.mkv", 8_280_000_000, false),
        }, info.Files);
    }

    [Fact]
    public void A_single_file_torrent_is_one_file_named_after_the_torrent()
    {
        Assert.True(TorrentMetainfo.TryParse(SingleFile("ubuntu.iso", 6_100_000_000), out var info));

        Assert.Equal(new MetainfoFile(0, "ubuntu.iso", 6_100_000_000, false), Assert.Single(info!.Files));
    }

    [Fact]
    public void The_utf8_path_wins_over_the_legacy_path()
    {
        var torrent = Encode(new Dictionary<string, object>
        {
            ["info"] = new Dictionary<string, object>
            {
                ["name"] = "T",
                ["files"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["length"] = 1L,
                        ["path"] = new List<object> { Encoding.Latin1.GetBytes("café.txt") },
                        ["path.utf-8"] = new List<object> { "café.txt" },
                    },
                    new Dictionary<string, object> { ["length"] = 2L, ["path"] = new List<object> { "b.txt" } },
                },
            },
        });

        Assert.True(TorrentMetainfo.TryParse(torrent, out var info));
        Assert.Equal(new[] { "T/café.txt", "T/b.txt" }, info!.Files.Select(f => f.Path));
    }

    [Fact]
    public void Padding_files_keep_their_index_and_are_marked()
    {
        var torrent = Encode(new Dictionary<string, object>
        {
            ["info"] = new Dictionary<string, object>
            {
                ["name"] = "T",
                ["files"] = new List<object>
                {
                    new Dictionary<string, object> { ["length"] = 10L, ["path"] = new List<object> { "a.bin" } },
                    new Dictionary<string, object> { ["length"] = 6L, ["attr"] = "p", ["path"] = new List<object> { ".pad", "6" } },
                    new Dictionary<string, object> { ["length"] = 4L, ["path"] = new List<object> { "b.bin" } },
                },
            },
        });

        Assert.True(TorrentMetainfo.TryParse(torrent, out var info));
        Assert.Equal(new[] { false, true, false }, info!.Files.Select(f => f.IsPadding));
        Assert.Equal(2, info.Files[2].Index);
    }

    [Fact]
    public void A_v2_only_torrent_is_not_supported()
    {
        var torrent = Encode(new Dictionary<string, object>
        {
            ["info"] = new Dictionary<string, object>
            {
                ["name"] = "T",
                ["meta version"] = 2L,
                ["file tree"] = new Dictionary<string, object>
                {
                    ["a.bin"] = new Dictionary<string, object> { [""] = new Dictionary<string, object> { ["length"] = 10L } },
                },
            },
        });

        Assert.False(TorrentMetainfo.TryParse(torrent, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not bencode")]
    [InlineData("d4:info")]                     // cut off
    [InlineData("d4:infod4:name1:T6:lengthi5eee1")] // trailing data
    [InlineData("d4:infod4:name999:Tee")]        // string longer than the file
    [InlineData("le")]                           // not a dictionary
    [InlineData("d4:infod6:lengthi5eee")]        // no name
    public void Anything_that_is_not_a_readable_torrent_gives_false(string text)
    {
        Assert.False(TorrentMetainfo.TryParse(Encoding.ASCII.GetBytes(text), out var info));
        Assert.Null(info);
    }

    [Fact]
    public void Deeply_nested_input_is_rejected_instead_of_overflowing_the_stack()
    {
        var hostile = Encoding.ASCII.GetBytes(new string('l', 10_000) + new string('e', 10_000));

        Assert.False(TorrentMetainfo.TryParse(hostile, out _));
    }
}
