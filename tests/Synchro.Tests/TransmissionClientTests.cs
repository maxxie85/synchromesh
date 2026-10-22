using Synchro.Core.Servers;
using Synchro.Core.Torrents;
using Synchro.Transmission;
using Xunit;

namespace Synchro.Tests;

public class TransmissionClientTests
{
    private const string TorrentGetJson = """
        {"jsonrpc":"2.0","id":1,"result":{
          "torrents":[{"id":3,"hash_string":"abc123","name":"Ubuntu 26.04","status":4,"percent_done":0.5,
            "total_size":4000000000,"rate_download":2000000,"rate_upload":100000,"eta":600,"upload_ratio":0.25,
            "error":0,"error_string":"","is_stalled":false,"added_date":1700000000,"labels":["linux","iso"]}],
          "removed":[7,9]}}
        """;

    private const string StatsJson = """
        {"jsonrpc":"2.0","id":1,"result":{"torrent_count":12,"active_torrent_count":3,"paused_torrent_count":2,
          "download_speed":4300000,"upload_speed":200000}}
        """;

    private static TransmissionClient Create(FakeHttpHandler fake) =>
        new(new TransmissionConnectionFactory(() => fake).Create(new ServerProfile(Guid.NewGuid(), "NAS", "nas"), null));

    [Fact]
    public async Task Full_fetch_asks_for_all_torrents_and_maps_every_field()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(TorrentGetJson));

        var update = await Create(fake).GetTorrentsAsync(recentlyActiveOnly: false);

        var body = fake.Calls.Single().Body!;
        Assert.Contains("\"method\":\"torrent_get\"", body);
        Assert.Contains("hash_string", body);
        Assert.DoesNotContain("\"ids\"", body);

        Assert.True(update.IsFullRefresh);
        var t = Assert.Single(update.Torrents);
        Assert.Equal(3, t.Id);
        Assert.Equal("abc123", t.HashString);
        Assert.Equal("Ubuntu 26.04", t.Name);
        Assert.Equal(TorrentStatus.Downloading, t.Status);
        Assert.Equal(0.5, t.PercentDone);
        Assert.Equal(4_000_000_000, t.TotalSize);
        Assert.Equal(2_000_000, t.RateDownload);
        Assert.Equal(600, t.Eta);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), t.AddedDate);
        Assert.Equal(new[] { "linux", "iso" }, t.LabelList);
    }

    [Fact]
    public async Task Delta_fetch_uses_recently_active_and_returns_removed_ids()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(TorrentGetJson));

        var update = await Create(fake).GetTorrentsAsync(recentlyActiveOnly: true);

        Assert.Contains("\"ids\":\"recently_active\"", fake.Calls.Single().Body);
        Assert.False(update.IsFullRefresh);
        Assert.Equal(new[] { 7, 9 }, update.RemovedIds);
    }

    [Fact]
    public async Task Unknown_status_numbers_fall_back_to_stopped()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(
            TorrentGetJson.Replace("\"status\":4", "\"status\":42")));

        var update = await Create(fake).GetTorrentsAsync(false);

        Assert.Equal(TorrentStatus.Stopped, update.Torrents.Single().Status);
    }

    [Fact]
    public async Task Session_stats_are_mapped()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(StatsJson));

        var stats = await Create(fake).GetSessionStatsAsync();

        Assert.Contains("\"method\":\"session_stats\"", fake.Calls.Single().Body);
        Assert.Equal(new SessionStats(12, 3, 2, 4_300_000, 200_000), stats);
    }

    [Fact]
    public async Task Session_totals_are_mapped_when_present()
    {
        var json = StatsJson.Replace("\"upload_speed\":200000", """
            "upload_speed":200000,
            "current_stats":{"uploaded_bytes":23090000000,"downloaded_bytes":2290000000,"files_added":3,"seconds_active":86400,"session_count":1},
            "cumulative_stats":{"uploaded_bytes":9698000000000,"downloaded_bytes":2336000000000,"files_added":6456,"seconds_active":108086400,"session_count":42}
            """);
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(json));

        var stats = await Create(fake).GetSessionStatsAsync();

        Assert.Equal(new StatsTotals(2_290_000_000, 23_090_000_000, 3, 86_400, 1), stats.Current);
        Assert.Equal(new StatsTotals(2_336_000_000_000, 9_698_000_000_000, 6456, 108_086_400, 42), stats.Cumulative);
    }

    private const string EmptyResultJson = """{"jsonrpc":"2.0","result":{},"id":1}""";

    [Fact]
    public async Task Maps_the_queue_position()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(
            TorrentGetJson.Replace("\"added_date\":1700000000", "\"added_date\":1700000000,\"queue_position\":4")));

        var update = await Create(fake).GetTorrentsAsync(false);

        Assert.Equal(4, update.Torrents.Single().QueuePosition);
        Assert.Contains("queue_position", fake.Calls.Single().Body);
    }

    [Theory]
    [InlineData(false, "torrent_start")]
    [InlineData(true, "torrent_start_now")]
    public async Task Start_sends_hashes_as_ids(bool startNow, string method)
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(EmptyResultJson));

        await Create(fake).StartAsync(["abc", "def"], startNow);

        var body = fake.Calls.Single().Body!;
        Assert.Contains($"\"method\":\"{method}\"", body);
        Assert.Contains("\"ids\":[\"abc\",\"def\"]", body);
    }

    [Fact]
    public async Task Stop_sends_hashes_as_ids()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(EmptyResultJson));

        await Create(fake).StopAsync(["abc"]);

        var body = fake.Calls.Single().Body!;
        Assert.Contains("\"method\":\"torrent_stop\"", body);
        Assert.Contains("\"ids\":[\"abc\"]", body);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Remove_says_whether_to_delete_the_data(bool deleteData)
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(EmptyResultJson));

        await Create(fake).RemoveAsync(["abc"], deleteData);

        var body = fake.Calls.Single().Body!;
        Assert.Contains("\"method\":\"torrent_remove\"", body);
        Assert.Contains($"\"delete_local_data\":{deleteData.ToString().ToLowerInvariant()}", body);
    }

    [Theory]
    [InlineData(QueueMove.Top, "queue_move_top")]
    [InlineData(QueueMove.Up, "queue_move_up")]
    [InlineData(QueueMove.Down, "queue_move_down")]
    [InlineData(QueueMove.Bottom, "queue_move_bottom")]
    public async Task Queue_moves_use_the_matching_method(QueueMove move, string method)
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(EmptyResultJson));

        await Create(fake).MoveInQueueAsync(["abc"], move);

        Assert.Contains($"\"method\":\"{method}\"", fake.Calls.Single().Body);
    }

    [Fact]
    public async Task Actions_with_no_torrents_send_nothing()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(EmptyResultJson));
        var client = Create(fake);

        await client.StartAsync([], false);
        await client.StopAsync([]);
        await client.RemoveAsync([], true);
        await client.MoveInQueueAsync([], QueueMove.Top);

        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task Actions_accept_a_response_without_a_result_object()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"jsonrpc":"2.0","id":1}"""));

        await Create(fake).StopAsync(["abc"]); // must not throw
    }

    [Fact]
    public async Task Speed_limits_are_read_from_the_session()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json("""
            {"jsonrpc":"2.0","id":1,"result":{"speed_limit_down":1500,"speed_limit_down_enabled":true,
             "speed_limit_up":200.0,"speed_limit_up_enabled":false,"alt_speed_enabled":true}}
            """));

        var limits = await Create(fake).GetSpeedLimitsAsync();

        Assert.Contains("\"method\":\"session_get\"", fake.Calls.Single().Body);
        Assert.Equal(new SpeedLimits(true, 1500, false, 200, true), limits);
    }

    [Fact]
    public async Task Speed_limits_are_written_with_session_set()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(EmptyResultJson));

        await Create(fake).SetSpeedLimitsAsync(new SpeedLimits(true, 1500, false, 200, true));

        var body = fake.Calls.Single().Body!;
        Assert.Contains("\"method\":\"session_set\"", body);
        Assert.Contains("\"speed_limit_down\":1500", body);
        Assert.Contains("\"speed_limit_down_enabled\":true", body);
        Assert.Contains("\"speed_limit_up\":200", body);
        Assert.Contains("\"speed_limit_up_enabled\":false", body);
        Assert.Contains("\"alt_speed_enabled\":true", body);
    }

    private static string AddedJson(string key = "torrent_added") =>
        """{"jsonrpc":"2.0","id":1,"result":{"KEY":{"id":5,"name":"Ubuntu","hash_string":"abc123"}}}""".Replace("KEY", key);

    [Fact]
    public async Task A_magnet_link_is_sent_as_filename()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(AddedJson()));
        var request = new AddTorrentRequest(new AddTorrentSource.Magnet("magnet:?xt=urn:btih:abc", null), null, false);

        var result = await Create(fake).AddTorrentAsync(request);

        var body = fake.Calls.Single().Body!;
        Assert.Contains("\"method\":\"torrent_add\"", body);
        Assert.Contains("\"filename\":\"magnet:?xt=urn:btih:abc\"", body);
        Assert.DoesNotContain("metainfo", body);
        Assert.DoesNotContain("download_dir", body);
        Assert.Contains("\"paused\":false", body);
        Assert.Equal(new AddTorrentResult(AddTorrentOutcome.Added, "abc123", "Ubuntu"), result);
    }

    [Fact]
    public async Task A_torrent_file_is_sent_base64_encoded_as_metainfo()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(AddedJson()));
        var content = new byte[] { 1, 2, 3, 250 };
        var request = new AddTorrentRequest(new AddTorrentSource.TorrentFile("a.torrent", content), null, false);

        await Create(fake).AddTorrentAsync(request);

        var body = fake.Calls.Single().Body!;

        // Compare the decoded JSON value: the serializer may escape characters such as '+' as \u002B.
        using var json = System.Text.Json.JsonDocument.Parse(body);
        var parameters = json.RootElement.GetProperty("params");
        Assert.Equal(Convert.ToBase64String(content), parameters.GetProperty("metainfo").GetString());
        Assert.False(parameters.TryGetProperty("filename", out _));
    }

    [Fact]
    public async Task A_url_is_sent_as_filename_too()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(AddedJson()));
        var request = new AddTorrentRequest(new AddTorrentSource.Url(new Uri("https://example.org/a.torrent")), null, false);

        await Create(fake).AddTorrentAsync(request);

        Assert.Contains("\"filename\":\"https://example.org/a.torrent\"", fake.Calls.Single().Body);
    }

    [Fact]
    public async Task Sequential_download_is_only_sent_when_asked_for()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(AddedJson()));
        var magnet = new AddTorrentSource.Magnet("magnet:?xt=urn:btih:abc", null);

        await Create(fake).AddTorrentAsync(new AddTorrentRequest(magnet, null, false));
        await Create(fake).AddTorrentAsync(new AddTorrentRequest(magnet, null, false, SequentialDownload: true));

        using var plain = System.Text.Json.JsonDocument.Parse(fake.Calls[0].Body!);
        Assert.False(plain.RootElement.GetProperty("params").TryGetProperty("sequential_download", out _));
        using var ordered = System.Text.Json.JsonDocument.Parse(fake.Calls[1].Body!);
        Assert.True(ordered.RootElement.GetProperty("params").GetProperty("sequential_download").GetBoolean());
    }

    [Fact]
    public async Task Folder_and_paused_are_sent_when_chosen()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(AddedJson()));
        var request = new AddTorrentRequest(new AddTorrentSource.Magnet("magnet:?xt=urn:btih:abc", null), "/data/movies", true);

        await Create(fake).AddTorrentAsync(request);

        var body = fake.Calls.Single().Body!;
        Assert.Contains("\"download_dir\":\"/data/movies\"", body);
        Assert.Contains("\"paused\":true", body);
    }

    [Fact]
    public async Task A_duplicate_is_a_result_not_an_error()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(AddedJson("torrent_duplicate")));
        var request = new AddTorrentRequest(new AddTorrentSource.Magnet("magnet:?xt=urn:btih:abc", null), null, false);

        var result = await Create(fake).AddTorrentAsync(request);

        Assert.Equal(AddTorrentOutcome.Duplicate, result.Outcome);
        Assert.Equal("Ubuntu", result.Name);
    }

    [Fact]
    public async Task An_answer_that_is_neither_added_nor_duplicate_is_reported()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"jsonrpc":"2.0","id":1,"result":{}}"""));
        var request = new AddTorrentRequest(new AddTorrentSource.Magnet("magnet:?xt=urn:btih:abc", null), null, false);

        await Assert.ThrowsAsync<Synchro.Transmission.JsonRpc.RpcProtocolException>(() => Create(fake).AddTorrentAsync(request));
    }

    [Fact]
    public async Task The_default_download_folder_comes_from_the_session()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"jsonrpc":"2.0","id":1,"result":{"download_dir":"/downloads"}}"""));

        var dir = await Create(fake).GetDefaultDownloadDirectoryAsync();

        Assert.Equal("/downloads", dir);
        Assert.Contains("download_dir", fake.Calls.Single().Body);
    }

    [Fact]
    public async Task Free_space_is_requested_for_a_path()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(
            """{"jsonrpc":"2.0","id":1,"result":{"path":"/downloads","size_bytes":5000000000,"total_size":8000000000}}"""));

        var free = await Create(fake).GetFreeSpaceAsync("/downloads");

        var body = fake.Calls.Single().Body!;
        Assert.Contains("\"method\":\"free_space\"", body);
        Assert.Contains("\"path\":\"/downloads\"", body);
        Assert.Equal(5_000_000_000, free);
    }

    private const string DetailsJson = """
        {"jsonrpc":"2.0","id":1,"result":{"torrents":[{
          "hash_string":"abc123","name":"Ubuntu","status":6,"is_stalled":false,"percent_done":1,
          "error":0,"error_string":"","download_dir":"/data/done","total_size":5000,"size_when_done":5000,
          "left_until_done":0,"have_valid":5000,"downloaded_ever":5100,"uploaded_ever":2500,"corrupt_ever":100,
          "upload_ratio":0.5,"rate_download":0,"rate_upload":2000,"eta":-1,
          "download_limited":false,"download_limit":100,"upload_limited":true,"upload_limit":250.0,
          "peers_connected":3,"peers_sending_to_us":0,"peers_getting_from_us":2,"peer_limit":60,
          "added_date":1700000000,"done_date":1700000600,"activity_date":1700001000,"date_created":0,
          "creator":"mktorrent","comment":"","piece_count":10,"piece_size":512,"is_private":true,
          "honors_session_limits":false,"bandwidth_priority":1,"seed_ratio_mode":1,"seed_ratio_limit":2.5,
          "seed_idle_mode":2,"seed_idle_limit":30,"sequential_download":true,
          "magnet_link":"magnet:?xt=urn:btih:abc123",
          "tracker_stats":[{"id":4,"tier":1,"announce":"udp://t.example:6969/announce","host":"t.example:6969",
            "is_backup":false,"announce_state":1,"has_announced":true,"last_announce_succeeded":false,
            "last_announce_timed_out":false,"last_announce_result":"could not connect to tracker",
            "next_announce_time":1700002000,"seeder_count":-1,"leecher_count":-1,"download_count":-1,
            "last_announce_peer_count":0}],
          "peers":[{"address":"10.0.0.2","port":51413,"client_name":"Transmission 4.0.6","flag_str":"TDE",
            "progress":0.25,"rate_to_client":0,"rate_to_peer":1500,"is_encrypted":true,"is_incoming":false,"is_utp":true}]
        }]}}
        """;

    [Fact]
    public async Task Details_are_requested_for_one_hash_and_mapped()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(DetailsJson));

        var d = await Create(fake).GetTorrentDetailsAsync("abc123");

        using var request = System.Text.Json.JsonDocument.Parse(fake.Calls.Single().Body!);
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("torrent_get", request.RootElement.GetProperty("method").GetString());
        Assert.Equal(new[] { "abc123" }, parameters.GetProperty("ids").EnumerateArray().Select(e => e.GetString()));
        var fields = parameters.GetProperty("fields").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("tracker_stats", fields);
        Assert.Contains("peers", fields);

        Assert.NotNull(d);
        Assert.Equal(TorrentStatus.Seeding, d.Status);
        Assert.Equal("/data/done", d.DownloadDirectory);
        Assert.Equal(2500, d.UploadedEver);
        Assert.True(d.UploadLimited);
        Assert.Equal(250, d.UploadLimitKilobytes);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_600), d.DoneDate);
        Assert.Null(d.DateCreated); // 0 means "never"
        Assert.True(d.IsPrivate);
        Assert.Equal(new TorrentOptions(false, 100, true, 250, false, BandwidthPriority.High,
            SeedLimitMode.Custom, 2.5, SeedLimitMode.Unlimited, 30, 60, SequentialDownload: true), d.Options);

        var tracker = Assert.Single(d.Trackers);
        Assert.Equal(4, tracker.Id);
        Assert.Equal(TrackerAnnounceState.Waiting, tracker.AnnounceState);
        Assert.False(tracker.LastAnnounceSucceeded);
        Assert.Equal("could not connect to tracker", tracker.LastAnnounceResult);
        Assert.Equal(-1, tracker.SeederCount);

        var peer = Assert.Single(d.Peers);
        Assert.Equal("10.0.0.2", peer.Address);
        Assert.Equal("TDE", peer.FlagString);
        Assert.Equal(1500, peer.RateToPeer);
        Assert.True(peer.IsEncrypted);
    }

    [Fact]
    public async Task Details_of_a_torrent_the_server_no_longer_has_are_null()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"jsonrpc":"2.0","id":1,"result":{"torrents":[]}}"""));

        Assert.Null(await Create(fake).GetTorrentDetailsAsync("gone"));
    }

    [Fact]
    public async Task Files_are_only_requested_when_asked_and_mapped_by_position()
    {
        var json = DetailsJson.Replace("\"peers\":[", "\"files\":[{\"name\":\"Ubuntu/a.iso\",\"length\":4000,\"bytes_completed\":1000,\"begin_piece\":0,\"end_piece\":7},{\"name\":\"Ubuntu/b.txt\",\"length\":10,\"bytes_completed\":10,\"begin_piece\":7,\"end_piece\":8}],\"file_stats\":[{\"bytes_completed\":1000,\"wanted\":true,\"priority\":1},{\"bytes_completed\":10,\"wanted\":false,\"priority\":-1}],\"peers\":[");
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(json));
        var client = Create(fake);

        var without = await client.GetTorrentDetailsAsync("abc123");
        var with = await client.GetTorrentDetailsAsync("abc123", DetailFiles.NamesAndProgress);

        Assert.Null(without!.Files);
        Assert.DoesNotContain("file_stats", Fields(fake.Calls[0].Body!));
        Assert.Contains("file_stats", Fields(fake.Calls[1].Body!));
        Assert.Collection(with!.Files!,
            f => Assert.Equal(new TorrentFile(0, "Ubuntu/a.iso", 4000, 1000, true, FilePriority.High), f),
            f => Assert.Equal(new TorrentFile(1, "Ubuntu/b.txt", 10, 10, false, FilePriority.Low), f));
    }

    [Fact]
    public async Task Progress_only_asks_for_file_stats_without_the_names()
    {
        var json = DetailsJson.Replace("\"peers\":[", "\"file_stats\":[{\"bytes_completed\":1000,\"wanted\":true,\"priority\":1},{\"bytes_completed\":10,\"wanted\":false,\"priority\":-1}],\"peers\":[");
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(json));

        var details = await Create(fake).GetTorrentDetailsAsync("abc123", DetailFiles.ProgressOnly);

        var fields = Fields(fake.Calls[0].Body!);
        Assert.Contains("file_stats", fields);
        Assert.DoesNotContain("files", fields);
        Assert.Null(details!.Files);
        Assert.Equal(new[] { new TorrentFileProgress(1000, true, FilePriority.High), new TorrentFileProgress(10, false, FilePriority.Low) },
            details.FileProgress);
    }

    [Fact]
    public async Task Specific_torrents_are_fetched_by_hash()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(TorrentGetJson));

        var update = await Create(fake).GetTorrentsAsync(["aaa", "bbb"]);

        using var body = System.Text.Json.JsonDocument.Parse(fake.Calls.Single().Body!);
        Assert.Equal(new[] { "aaa", "bbb" }, body.RootElement.GetProperty("params").GetProperty("ids").EnumerateArray().Select(e => e.GetString()));
        Assert.False(update.IsFullRefresh);
        Assert.Empty(update.RemovedIds);
    }

    [Theory]
    [InlineData(true, "files_wanted", "files_unwanted")]
    [InlineData(false, "files_unwanted", "files_wanted")]
    public async Task Wanted_files_are_sent_by_index_in_the_right_list(bool wanted, string sent, string absent)
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"jsonrpc":"2.0","id":1,"result":{}}"""));

        await Create(fake).SetFilesWantedAsync("abc", [2, 5], wanted);

        using var request = System.Text.Json.JsonDocument.Parse(fake.Calls.Single().Body!);
        var parameters = request.RootElement.GetProperty("params");
        Assert.Equal("torrent_set", request.RootElement.GetProperty("method").GetString());
        Assert.Equal(new[] { "abc" }, parameters.GetProperty("ids").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(new[] { 2, 5 }, parameters.GetProperty(sent).EnumerateArray().Select(e => e.GetInt32()));
        Assert.False(parameters.TryGetProperty(absent, out _));
    }

    [Theory]
    [InlineData(FilePriority.High, "priority_high")]
    [InlineData(FilePriority.Normal, "priority_normal")]
    [InlineData(FilePriority.Low, "priority_low")]
    public async Task Priority_is_sent_in_the_matching_list_only(FilePriority priority, string key)
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"jsonrpc":"2.0","id":1,"result":{}}"""));

        await Create(fake).SetFilePriorityAsync("abc", [1], priority);

        using var request = System.Text.Json.JsonDocument.Parse(fake.Calls.Single().Body!);
        var names = request.RootElement.GetProperty("params").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "ids", key }, names);
    }

    [Fact]
    public async Task An_empty_file_selection_is_never_sent_because_it_would_mean_all_files()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"jsonrpc":"2.0","id":1,"result":{}}"""));
        var client = Create(fake);

        await client.SetFilesWantedAsync("abc", [], wanted: false);
        await client.SetFilePriorityAsync("abc", [], FilePriority.Low);

        Assert.Empty(fake.Calls);
    }

    private static List<string?> Fields(string body)
    {
        using var request = System.Text.Json.JsonDocument.Parse(body);
        return request.RootElement.GetProperty("params").GetProperty("fields").EnumerateArray().Select(e => e.GetString()).ToList();
    }

    [Fact]
    public async Task File_choices_are_sent_with_the_add_and_empty_lists_are_left_out()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(AddedJson()));
        var request = new AddTorrentRequest(new AddTorrentSource.TorrentFile("t.torrent", [1, 2, 3]), null, false,
            new FileChoices(Unwanted: [1, 4], HighPriority: [0], LowPriority: []));

        await Create(fake).AddTorrentAsync(request);

        using var body = System.Text.Json.JsonDocument.Parse(fake.Calls.Single().Body!);
        var parameters = body.RootElement.GetProperty("params");
        Assert.Equal(new[] { 1, 4 }, parameters.GetProperty("files_unwanted").EnumerateArray().Select(e => e.GetInt32()));
        Assert.Equal(new[] { 0 }, parameters.GetProperty("priority_high").EnumerateArray().Select(e => e.GetInt32()));
        Assert.False(parameters.TryGetProperty("priority_low", out _));
        Assert.False(parameters.TryGetProperty("files_wanted", out _));
    }

    [Fact]
    public async Task The_file_list_reports_how_far_the_metadata_is()
    {
        var json = """{"jsonrpc":"2.0","id":1,"result":{"torrents":[{"hash_string":"abc","name":"Ubuntu","metadata_percent_complete":0.25,"files":[],"file_stats":[]}]}}""";
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(json));

        var list = await Create(fake).GetFileListAsync("abc");

        Assert.Equal(0.25, list!.MetadataPercentComplete);
        Assert.False(list.HasMetadata);
        Assert.Contains("metadata_percent_complete", Fields(fake.Calls.Single().Body!));
    }

    [Fact]
    public async Task Storage_reports_free_and_total_space()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(
            """{"jsonrpc":"2.0","id":1,"result":{"path":"/data","size_bytes":93460000000,"total_size":2000000000000}}"""));

        var storage = await Create(fake).GetStorageAsync("/data");

        Assert.Equal(new StorageInfo("/data", 93_460_000_000, 2_000_000_000_000), storage);
        Assert.Equal(1_906_540_000_000, storage.UsedBytes);
    }

    [Fact]
    public async Task Storage_without_a_total_only_has_free_space()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json(
            """{"jsonrpc":"2.0","id":1,"result":{"path":"/data","size_bytes":500,"total_size":-1}}"""));

        var storage = await Create(fake).GetStorageAsync("/data");

        Assert.False(storage.HasTotal);
        Assert.Equal(500, storage.FreeBytes);
        Assert.Equal(0, storage.UsedBytes);
    }

    [Theory]
    [InlineData("verify", "torrent_verify")]
    [InlineData("reannounce", "torrent_reannounce")]
    public async Task Verify_and_reannounce_address_torrents_by_hash(string action, string method)
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"jsonrpc":"2.0","id":1,"result":{}}"""));
        var client = Create(fake);

        await (action == "verify" ? client.VerifyAsync(["a", "b"]) : client.ReannounceAsync(["a", "b"]));

        using var body = System.Text.Json.JsonDocument.Parse(fake.Calls.Single().Body!);
        Assert.Equal(method, body.RootElement.GetProperty("method").GetString());
        Assert.Equal(new[] { "a", "b" }, body.RootElement.GetProperty("params").GetProperty("ids").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Alternative_speed_limits_are_read_and_switched_on_their_own()
    {
        var fake = new FakeHttpHandler(call => FakeHttpHandler.Json(call.Body!.Contains("session_get")
            ? """{"jsonrpc":"2.0","id":1,"result":{"alt_speed_enabled":true,"alt_speed_down":50,"alt_speed_up":20.0}}"""
            : """{"jsonrpc":"2.0","id":1,"result":{}}"""));
        var client = Create(fake);

        var alt = await client.GetAltSpeedAsync();
        await client.SetAltSpeedEnabledAsync(false);

        Assert.Equal(new AltSpeed(true, 50, 20), alt);
        using var set = System.Text.Json.JsonDocument.Parse(fake.Calls[1].Body!);
        var parameters = set.RootElement.GetProperty("params");
        Assert.False(parameters.GetProperty("alt_speed_enabled").GetBoolean());
        Assert.Single(parameters.EnumerateObject()); // nothing else is changed
    }

    [Fact]
    public async Task Torrent_options_are_sent_with_torrent_set_for_one_hash()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"jsonrpc":"2.0","id":1,"result":{}}"""));

        await Create(fake).SetTorrentOptionsAsync("abc", new TorrentOptions(true, 500, false, 0, true,
            BandwidthPriority.Low, SeedLimitMode.Custom, 1.5, SeedLimitMode.ServerDefault, 45, 80, SequentialDownload: true));

        using var body = System.Text.Json.JsonDocument.Parse(fake.Calls.Single().Body!);
        var p = body.RootElement.GetProperty("params");
        Assert.Equal("torrent_set", body.RootElement.GetProperty("method").GetString());
        Assert.Equal("abc", p.GetProperty("ids")[0].GetString());
        Assert.True(p.GetProperty("download_limited").GetBoolean());
        Assert.Equal(500, p.GetProperty("download_limit").GetInt32());
        Assert.Equal(-1, p.GetProperty("bandwidth_priority").GetInt32());
        Assert.Equal(1, p.GetProperty("seed_ratio_mode").GetInt32());
        Assert.Equal(1.5, p.GetProperty("seed_ratio_limit").GetDouble());
        Assert.Equal(0, p.GetProperty("seed_idle_mode").GetInt32());
        Assert.Equal(45, p.GetProperty("seed_idle_limit").GetInt32());
        Assert.Equal(80, p.GetProperty("peer_limit").GetInt32());
        Assert.True(p.GetProperty("sequential_download").GetBoolean());
        Assert.True(p.GetProperty("honors_session_limits").GetBoolean());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Moving_data_uses_set_location_with_the_move_flag(bool move)
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"jsonrpc":"2.0","id":1,"result":{}}"""));

        await Create(fake).SetLocationAsync(["a", "b"], "/data/movies", move);

        using var body = System.Text.Json.JsonDocument.Parse(fake.Calls.Single().Body!);
        var p = body.RootElement.GetProperty("params");
        Assert.Equal("torrent_set_location", body.RootElement.GetProperty("method").GetString());
        Assert.Equal("/data/movies", p.GetProperty("location").GetString());
        Assert.Equal(move, p.GetProperty("move").GetBoolean());
        Assert.Equal(2, p.GetProperty("ids").GetArrayLength());
    }

    [Fact]
    public async Task Labels_are_replaced_with_torrent_set_and_an_empty_list_clears_them()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"jsonrpc":"2.0","id":1,"result":{}}"""));
        var client = Create(fake);

        await client.SetLabelsAsync(["a"], ["movies", "4k"]);
        await client.SetLabelsAsync(["b"], []);

        using var first = System.Text.Json.JsonDocument.Parse(fake.Calls[0].Body!);
        Assert.Equal(new[] { "movies", "4k" }, first.RootElement.GetProperty("params").GetProperty("labels").EnumerateArray().Select(e => e.GetString()));
        using var second = System.Text.Json.JsonDocument.Parse(fake.Calls[1].Body!);
        Assert.Equal(0, second.RootElement.GetProperty("params").GetProperty("labels").GetArrayLength());
    }

    [Fact]
    public async Task Trackers_are_sent_as_a_tracker_list_with_blank_lines_between_tiers()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"jsonrpc":"2.0","id":1,"result":{}}"""));
        var client = Create(fake);

        await client.SetTrackersAsync("abc", [["https://a/announce", "https://b/announce"], [], ["udp://c:80"]]);

        using var doc = System.Text.Json.JsonDocument.Parse(fake.Calls[0].Body!);
        Assert.Equal("torrent_set", doc.RootElement.GetProperty("method").GetString());
        var p = doc.RootElement.GetProperty("params");
        Assert.Equal("abc", p.GetProperty("ids")[0].GetString());
        Assert.Equal("https://a/announce\nhttps://b/announce\n\nudp://c:80", p.GetProperty("tracker_list").GetString());
    }

    [Fact]
    public async Task Rename_path_sends_one_torrent_its_path_and_the_new_name()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"jsonrpc":"2.0","id":1,"result":{"path":"T/a.mkv","name":"b.mkv","id":1}}"""));
        var client = Create(fake);

        await client.RenamePathAsync("abc", "T/a.mkv", "b.mkv");

        using var doc = System.Text.Json.JsonDocument.Parse(fake.Calls[0].Body!);
        Assert.Equal("torrent_rename_path", doc.RootElement.GetProperty("method").GetString());
        var p = doc.RootElement.GetProperty("params");
        Assert.Equal(new[] { "abc" }, p.GetProperty("ids").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("T/a.mkv", p.GetProperty("path").GetString());
        Assert.Equal("b.mkv", p.GetProperty("name").GetString());
    }

    private const string PreferencesJson = """
        {"jsonrpc":"2.0","id":1,"result":{
          "download_dir":"/data/done","incomplete_dir_enabled":true,"incomplete_dir":"/data/incoming",
          "start_added_torrents":true,"rename_partial_files":true,"trash_original_torrent_files":false,
          "download_queue_enabled":true,"download_queue_size":5.0,"seed_queue_enabled":false,"seed_queue_size":10,
          "seed_ratio_limited":true,"seed_ratio_limit":2.5,"idle_seeding_limit_enabled":false,"idle_seeding_limit":30,
          "peer_limit_global":200,"peer_limit_per_torrent":50,"encryption":"required","dht_enabled":true,
          "pex_enabled":true,"lpd_enabled":false,"peer_port":51413,"port_forwarding_enabled":true,
          "alt_speed_down":50,"alt_speed_up":20,"alt_speed_time_enabled":true,"alt_speed_time_begin":540,
          "alt_speed_time_end":1020,"alt_speed_time_day":62,
          "blocklist_enabled":true,"blocklist_url":"https://bl.example/level1.gz"}}
        """;

    [Fact]
    public async Task Server_preferences_are_read_and_written_with_the_protocols_names()
    {
        var fake = new FakeHttpHandler(call => FakeHttpHandler.Json(call.Body!.Contains("session_get")
            ? PreferencesJson : """{"jsonrpc":"2.0","id":1,"result":{}}"""));
        var client = Create(fake);

        var prefs = await client.GetServerPreferencesAsync();
        await client.SetServerPreferencesAsync(prefs with { Encryption = Synchro.Core.Servers.EncryptionMode.Allowed, PeerPort = 6881 });

        Assert.Equal("/data/incoming", prefs.IncompleteDirectory);
        Assert.Equal(Synchro.Core.Servers.EncryptionMode.Required, prefs.Encryption);
        Assert.Equal(5, prefs.DownloadQueueSize);
        Assert.Equal(2.5, prefs.SeedRatioLimit);
        Assert.Equal(540, prefs.AltSpeedTimeBegin);
        Assert.Equal(62, prefs.AltSpeedTimeDays);
        Assert.True(prefs.BlocklistEnabled);
        Assert.Equal("https://bl.example/level1.gz", prefs.BlocklistUrl);

        using var set = System.Text.Json.JsonDocument.Parse(fake.Calls[1].Body!);
        var p = set.RootElement.GetProperty("params");
        Assert.Equal("session_set", set.RootElement.GetProperty("method").GetString());
        Assert.Equal("allowed", p.GetProperty("encryption").GetString());
        Assert.Equal(6881, p.GetProperty("peer_port").GetInt32());
        Assert.Equal("/data/done", p.GetProperty("download_dir").GetString());
        Assert.Equal(62, p.GetProperty("alt_speed_time_day").GetInt32());
        Assert.True(p.GetProperty("blocklist_enabled").GetBoolean());
        Assert.Equal("https://bl.example/level1.gz", p.GetProperty("blocklist_url").GetString());
        Assert.False(p.TryGetProperty("blocklist_size", out _)); // read-only
    }

    [Fact]
    public async Task The_blocklist_size_is_read_and_an_update_returns_the_new_size()
    {
        var fake = new FakeHttpHandler(call => FakeHttpHandler.Json(call.Body!.Contains("blocklist_update")
            ? """{"jsonrpc":"2.0","id":1,"result":{"blocklist_size":250000}}"""
            : """{"jsonrpc":"2.0","id":1,"result":{"blocklist_size":1234.0}}"""));
        var client = Create(fake);

        Assert.Equal(1234, await client.GetBlocklistSizeAsync());
        Assert.Equal(250000, await client.UpdateBlocklistAsync());

        using var get = System.Text.Json.JsonDocument.Parse(fake.Calls[0].Body!);
        Assert.Equal("blocklist_size", get.RootElement.GetProperty("params").GetProperty("fields")[0].GetString());
        using var update = System.Text.Json.JsonDocument.Parse(fake.Calls[1].Body!);
        Assert.Equal("blocklist_update", update.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public async Task The_port_test_asks_for_one_ip_version()
    {
        var fake = new FakeHttpHandler(_ => FakeHttpHandler.Json("""{"jsonrpc":"2.0","id":1,"result":{"port_is_open":true,"ip_protocol":"ipv4"}}"""));

        var open = await Create(fake).TestPortAsync("ipv4");

        Assert.True(open);
        using var body = System.Text.Json.JsonDocument.Parse(fake.Calls.Single().Body!);
        Assert.Equal("port_test", body.RootElement.GetProperty("method").GetString());
        Assert.Equal("ipv4", body.RootElement.GetProperty("params").GetProperty("ip_protocol").GetString());
    }
}
