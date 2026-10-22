using System.Security.Cryptography;
using System.Text;
using NSubstitute;
using Synchro.Core.Servers;
using Synchro.Core.Torrents;

namespace Synchro.Screenshots;

/// <summary>
/// A made-up Transmission server for the screenshots, with only Linux installation images, so nobody's real torrents
/// end up in the README. Addresses are from the documentation ranges (RFC 5737, RFC 3849).
/// </summary>
internal static class DemoServer
{
    private const long MB = 1_000_000;
    private const long GB = 1_000_000_000;
    private static readonly DateTimeOffset Now = DateTimeOffset.Now;

    public const string Featured = "debian-12.8.0-amd64-DVD";

    private static readonly Torrent[] Torrents =
    [
        Make(1, "ubuntu-24.04.1-desktop-amd64.iso", TorrentStatus.Downloading, 0.63, 6_100 * MB, 8_400_000, 310_000, 0.12, ["desktop"]),
        Make(2, Featured, TorrentStatus.Downloading, 0.41, 13_310 * MB, 4_700_000, 120_000, 0.05, ["server"]),
        Make(3, "Fedora-Workstation-Live-x86_64-41-1.4.iso", TorrentStatus.Downloading, 0.28, 2_300 * MB, 3_100_000, 45_000, 0.02, ["desktop"]),
        Make(4, "linuxmint-22-cinnamon-64bit.iso", TorrentStatus.QueuedToDownload, 0, 2_900 * MB, 0, 0, 0, ["desktop"]),
        Make(5, "archlinux-2026.10.01-x86_64.iso", TorrentStatus.Seeding, 1, 1_200 * MB, 0, 640_000, 3.21, ["rolling"]),
        Make(6, "ubuntu-24.04.1-live-server-amd64.iso", TorrentStatus.Seeding, 1, 2_700 * MB, 0, 410_000, 1.84, ["server"]),
        Make(7, "Fedora-Server-dvd-x86_64-41-1.4.iso", TorrentStatus.Seeding, 1, 2_600 * MB, 0, 1_250_000, 0.97, ["server"]),
        Make(8, "openSUSE-Leap-15.6-DVD-x86_64-Media.iso", TorrentStatus.Seeding, 1, 4_400 * MB, 0, 95_000, 2.40, ["server"]),
        Make(9, "manjaro-kde-24.1.2-241104-linux610.iso", TorrentStatus.Seeding, 1, 3_900 * MB, 0, 0, 5.02, ["desktop", "rolling"]),
        Make(10, "kali-linux-2026.3-installer-amd64.iso", TorrentStatus.Stopped, 1, 4_000 * MB, 0, 0, 1.12, []),
        Make(11, "pop-os_24.04_amd64_intel_2.iso", TorrentStatus.Stopped, 0.87, 2_600 * MB, 0, 0, 0.31, ["desktop"]),
        Make(12, "AlmaLinux-9.5-x86_64-minimal.iso", TorrentStatus.Verifying, 0.54, 2_100 * MB, 0, 0, 0.88, ["server"]),
        Make(13, "openSUSE-Tumbleweed-DVD-x86_64-Current.iso", TorrentStatus.Stopped, 0.12, 4_700 * MB, 0, 0, 0, ["rolling"],
            error: "Tracker gave HTTP response code 404 (Not Found)"),
    ];

    public static ITransmissionClient Client()
    {
        var client = Substitute.For<ITransmissionClient>();
        client.Capabilities.Returns(ServerCapabilities.Baseline);
        client.GetTorrentsAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => new TorrentUpdate(Torrents, [], IsFullRefresh: true));
        client.GetTorrentsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => new TorrentUpdate(
                Torrents.Where(t => call.Arg<IReadOnlyCollection<string>>().Contains(t.HashString)).ToArray(), [], false));
        client.GetSessionStatsAsync(Arg.Any<CancellationToken>()).Returns(_ => Stats());
        client.GetTorrentDetailsAsync(Arg.Any<string>(), Arg.Any<DetailFiles>(), Arg.Any<CancellationToken>())
            .Returns(call => Details(call.Arg<string>(), call.Arg<DetailFiles>()));
        client.GetAltSpeedAsync(Arg.Any<CancellationToken>()).Returns(new AltSpeed(false, 500, 100));
        client.GetSpeedLimitsAsync(Arg.Any<CancellationToken>()).Returns(new SpeedLimits(false, 10_000, true, 2_000, false));
        client.GetServerPreferencesAsync(Arg.Any<CancellationToken>()).Returns(Preferences);
        client.GetBlocklistSizeAsync(Arg.Any<CancellationToken>()).Returns(412_384);
        client.TestPortAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        client.GetDefaultDownloadDirectoryAsync(Arg.Any<CancellationToken>()).Returns("/srv/downloads");
        client.GetFreeSpaceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(1_240 * GB);
        client.GetStorageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new StorageInfo(call.Arg<string>(), 1_240 * GB, 4_000 * GB));
        return client;
    }

    /// <summary>Three minutes of total speed for the sidebar graph, as if the app had been open a while.</summary>
    public static IEnumerable<ViewModels.SpeedSample> SpeedHistory()
    {
        var start = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(3);
        for (var i = 0; i < 90; i++)
        {
            var down = 12_000_000 + 4_500_000 * Math.Sin(i / 8.0) + 2_000_000 * Math.Sin(i / 2.7) + (i > 55 ? 3_000_000 : 0);
            var up = 2_300_000 + 700_000 * Math.Cos(i / 6.0) + 300_000 * Math.Sin(i / 1.9);
            yield return new ViewModels.SpeedSample(start + TimeSpan.FromSeconds(2 * i), (long)down, (long)up);
        }
    }

    public static ServerPreferences Preferences { get; } = new(
        DownloadDirectory: "/srv/downloads", IncompleteDirectoryEnabled: true, IncompleteDirectory: "/srv/incomplete",
        StartAddedTorrents: true, RenamePartialFiles: true, TrashOriginalTorrentFiles: false,
        DownloadQueueEnabled: true, DownloadQueueSize: 3, SeedQueueEnabled: false, SeedQueueSize: 10,
        SeedRatioLimited: true, SeedRatioLimit: 2.0, IdleSeedingLimitEnabled: false, IdleSeedingLimitMinutes: 30,
        PeerLimitGlobal: 240, PeerLimitPerTorrent: 60, Encryption: EncryptionMode.Preferred,
        DhtEnabled: true, PexEnabled: true, LpdEnabled: false, PeerPort: 51413, PortForwardingEnabled: true,
        AltSpeedDownKilobytes: 500, AltSpeedUpKilobytes: 100, AltSpeedTimeEnabled: true,
        AltSpeedTimeBegin: 8 * 60, AltSpeedTimeEnd: 18 * 60, AltSpeedTimeDays: 2 | 4 | 8 | 16 | 32,
        BlocklistEnabled: true, BlocklistUrl: "https://example.org/blocklist.gz");

    /// <summary>A multi-file .torrent for the Add dialog.</summary>
    public const string AddFileName = "AlmaLinux-9.5-x86_64.torrent";

    public static byte[] TorrentFile()
    {
        (string[] Path, long Length)[] files =
        [
            (["AlmaLinux-9.5-x86_64", "AlmaLinux-9.5-x86_64-dvd.iso"], 11_100 * MB),
            (["AlmaLinux-9.5-x86_64", "AlmaLinux-9.5-x86_64-boot.iso"], 1_000 * MB),
            (["AlmaLinux-9.5-x86_64", "AlmaLinux-9.5-x86_64-minimal.iso"], 2_100 * MB),
            (["AlmaLinux-9.5-x86_64", "checksums", "CHECKSUM"], 1_200),
            (["AlmaLinux-9.5-x86_64", "checksums", "CHECKSUM.asc"], 2_100),
            (["AlmaLinux-9.5-x86_64", "README.txt"], 3_000),
        ];
        const long pieceLength = 4 * MB;
        var pieces = (files.Sum(f => f.Length) + pieceLength - 1) / pieceLength;

        var info = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["files"] = files.Select(f => (object)new SortedDictionary<string, object>(StringComparer.Ordinal)
            {
                ["length"] = f.Length,
                ["path"] = f.Path.Cast<object>().ToList(),
            }).ToList(),
            ["name"] = "AlmaLinux-9.5-x86_64",
            ["piece length"] = pieceLength,
            ["pieces"] = new byte[pieces * 20],
        };
        var root = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["announce"] = "udp://tracker.example.org:1337/announce",
            ["comment"] = "AlmaLinux OS 9.5 for x86_64",
            ["created by"] = "Synchromesh screenshots",
            ["info"] = info,
        };
        using var output = new MemoryStream();
        Bencode(output, root);
        return output.ToArray();
    }

    private static void Bencode(Stream output, object value)
    {
        void Write(string text) => output.Write(Encoding.UTF8.GetBytes(text));
        switch (value)
        {
            case long number: Write($"i{number}e"); break;
            case string text: var bytes = Encoding.UTF8.GetBytes(text); Write($"{bytes.Length}:"); output.Write(bytes); break;
            case byte[] raw: Write($"{raw.Length}:"); output.Write(raw); break;
            case List<object> list: Write("l"); list.ForEach(item => Bencode(output, item)); Write("e"); break;
            case SortedDictionary<string, object> dict:
                Write("d");
                foreach (var (key, item) in dict) { Bencode(output, key); Bencode(output, item); }
                Write("e");
                break;
            default: throw new ArgumentException($"Can't bencode {value.GetType()}.");
        }
    }

    private static SessionStats Stats() => new(
        Torrents.Length, Torrents.Count(t => t.RateDownload + t.RateUpload > 0), Torrents.Count(t => t.Status == TorrentStatus.Stopped),
        Torrents.Sum(t => t.RateDownload) + Wobble(900_000), Torrents.Sum(t => t.RateUpload) + Wobble(200_000),
        Current: new StatsTotals(48 * GB, 31 * GB, 6, 3 * 86_400 + 4 * 3_600, 1),
        Cumulative: new StatsTotals(2_840 * GB, 6_910 * GB, 412, 410 * 86_400, 57));

    private static long Wobble(long size) => (long)(size * Math.Sin(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 3_000.0));

    private static TorrentDetails? Details(string hash, DetailFiles files)
    {
        if (Torrents.FirstOrDefault(t => t.HashString == hash) is not { } t) return null;

        var featured = t.Name == Featured;
        var fileList = featured ? DebianFiles(t.PercentDone) : [new TorrentFile(0, t.Name, t.TotalSize, (long)(t.TotalSize * t.PercentDone), true, FilePriority.Normal)];
        var have = (long)(t.TotalSize * t.PercentDone);
        return new TorrentDetails(
            t.HashString, t.Name, t.Status, t.IsStalled, t.PercentDone, t.Error, t.ErrorString,
            DownloadDirectory: featured ? "/srv/downloads/debian" : "/srv/downloads",
            TotalSize: t.TotalSize, SizeWhenDone: t.TotalSize, LeftUntilDone: t.TotalSize - have, HaveValid: have,
            DownloadedEver: have, UploadedEver: (long)(t.TotalSize * Math.Max(t.UploadRatio, 0)), CorruptEver: 0,
            UploadRatio: t.UploadRatio, RateDownload: t.RateDownload, RateUpload: t.RateUpload, Eta: t.Eta,
            DownloadLimited: false, DownloadLimitKilobytes: 100, UploadLimited: false, UploadLimitKilobytes: 100,
            PeersConnected: featured ? 38 : 12, PeersSendingToUs: featured ? 21 : 0, PeersGettingFromUs: featured ? 4 : 6,
            PeerLimit: 60, AddedDate: t.AddedDate, DoneDate: t.PercentDone >= 1 ? t.AddedDate.AddHours(2) : null,
            ActivityDate: Now.AddSeconds(-3), DateCreated: new DateTimeOffset(2024, 11, 9, 11, 34, 0, TimeSpan.Zero),
            Creator: "mktorrent 1.1", Comment: featured ? "\"Debian CD/DVD images\" (official)" : null,
            PieceCount: (int)(t.TotalSize / (4 * MB)) + 1, PieceSize: 4 * MB, IsPrivate: false,
            MagnetLink: $"magnet:?xt=urn:btih:{t.HashString}&dn={Uri.EscapeDataString(t.Name)}",
            Trackers: Trackers(t), Peers: featured ? Peers() : [],
            Files: files == DetailFiles.NamesAndProgress ? fileList : null,
            Options: new TorrentOptions(false, 100, false, 100, true, BandwidthPriority.Normal, SeedLimitMode.ServerDefault, 2,
                SeedLimitMode.ServerDefault, 30, 60),
            Labels: t.LabelList,
            FileProgress: files == DetailFiles.ProgressOnly
                ? fileList.Select(f => new TorrentFileProgress(f.BytesCompleted, f.Wanted, f.Priority)).ToArray()
                : null);
    }

    private static TorrentFile[] DebianFiles(double done) =>
    [
        new(0, $"{Featured}/debian-12.8.0-amd64-DVD-1.iso", 3_900 * MB, 3_900 * MB, true, FilePriority.High),
        new(1, $"{Featured}/debian-12.8.0-amd64-DVD-2.iso", 4_700 * MB, (long)(4_700 * MB * done * 0.35), true, FilePriority.Normal),
        new(2, $"{Featured}/debian-12.8.0-amd64-DVD-3.iso", 4_700 * MB, 0, false, FilePriority.Normal),
        new(3, $"{Featured}/checksums/SHA256SUMS", 400, 400, true, FilePriority.Normal),
        new(4, $"{Featured}/checksums/SHA256SUMS.sign", 900, 900, true, FilePriority.Normal),
        new(5, $"{Featured}/checksums/SHA512SUMS", 700, 0, true, FilePriority.Low),
        new(6, $"{Featured}/checksums/SHA512SUMS.sign", 900, 0, true, FilePriority.Low),
        new(7, $"{Featured}/README.txt", 1_200, 1_200, true, FilePriority.Normal),
    ];

    private static TrackerInfo[] Trackers(Torrent t) =>
    [
        new(0, 0, "udp://tracker.example.org:1337/announce", "tracker.example.org", false, TrackerAnnounceState.Waiting,
            true, !t.HasError, false, t.HasError ? t.ErrorString : "Success", Now.AddMinutes(24), 412, 37, 12_034, 50),
        new(1, 1, "https://tracker.example.net/announce", "tracker.example.net", false, TrackerAnnounceState.Waiting,
            true, true, false, "Success", Now.AddMinutes(11), 388, 29, 9_120, 50),
    ];

    private static PeerInfo[] Peers() =>
    [
        new("192.0.2.14", 51413, "Transmission 4.1.3", "DEI", 1, 1_240_000, 0, true, false, true),
        new("198.51.100.73", 6881, "qBittorrent 5.1.2", "DEX", 1, 980_000, 12_000, true, true, false),
        new("203.0.113.5", 51413, "Transmission 4.0.6", "dEI", 0.72, 640_000, 48_000, true, false, true),
        new("2001:db8::4f2a", 43211, "Deluge 2.1.1", "DE", 1, 510_000, 0, true, false, false),
        new("198.51.100.201", 6889, "libtorrent 2.0.11", "UEI", 0.33, 0, 36_000, false, true, true),
    ];

    private static Torrent Make(int id, string name, TorrentStatus status, double done, long size, long down, long up,
        double ratio, string[] labels, string? error = null)
    {
        var hash = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(name)));
        var eta = down > 0 ? (long)(size * (1 - done) / down) : -1;
        return new Torrent(id, hash, name, status, done, size, down, up, eta, ratio, error is null ? 0 : 2, error,
            false, Now.AddDays(-id * 1.7).AddHours(-id), id - 1, labels);
    }
}
