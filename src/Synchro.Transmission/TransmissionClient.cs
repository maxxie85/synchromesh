using Synchro.Core.Servers;
using Synchro.Core.Torrents;
using Synchro.Transmission.JsonRpc;

namespace Synchro.Transmission;

public sealed class TransmissionClient(TransmissionConnection connection, ServerCapabilities? capabilities = null)
    : ITransmissionClient
{
    public ServerCapabilities Capabilities { get; } = capabilities ?? ServerCapabilities.Baseline;

    // Only what the list view shows. The details panel has its own field list below.
    private static readonly string[] TorrentFields =
    [
        "id", "hash_string", "name", "status", "percent_done", "total_size",
        "rate_download", "rate_upload", "eta", "upload_ratio",
        "error", "error_string", "is_stalled", "added_date", "queue_position", "labels",
    ];

    // One torrent's details panel (General, Trackers, Peers), fetched for the selected torrent only.
    private static readonly string[] DetailFields =
    [
        "hash_string", "name", "status", "is_stalled", "percent_done", "error", "error_string", "download_dir",
        "total_size", "size_when_done", "left_until_done", "have_valid", "downloaded_ever", "uploaded_ever",
        "corrupt_ever", "upload_ratio", "rate_download", "rate_upload", "eta",
        "download_limited", "download_limit", "upload_limited", "upload_limit",
        "peers_connected", "peers_sending_to_us", "peers_getting_from_us", "peer_limit",
        "added_date", "done_date", "activity_date", "date_created", "creator", "comment",
        "piece_count", "piece_size", "is_private", "magnet_link", "tracker_stats", "peers",
        "honors_session_limits", "bandwidth_priority", "seed_ratio_mode", "seed_ratio_limit",
        "seed_idle_mode", "seed_idle_limit", "labels", "sequential_download",
    ];

    private static readonly string[] SpeedLimitFields =
    [
        "speed_limit_down", "speed_limit_down_enabled", "speed_limit_up", "speed_limit_up_enabled", "alt_speed_enabled",
    ];

    private sealed record TorrentGetParams(string[] Fields, object? Ids);
    private sealed record IdsParams(string[] Ids);
    private sealed record DetailGetParams(string[] Fields, string[] Ids);
    private sealed record SetLocationParams(string[] Ids, string Location, bool Move);
    private sealed record LabelsParams(string[] Ids, string[] Labels);
    private sealed record TrackerListParams(string[] Ids, string TrackerList);
    private sealed record RenamePathParams(string[] Ids, string Path, string Name);
    private sealed record TorrentOptionsParams(
        string[] Ids, bool DownloadLimited, int DownloadLimit, bool UploadLimited, int UploadLimit,
        bool HonorsSessionLimits, int BandwidthPriority, int SeedRatioMode, double SeedRatioLimit,
        int SeedIdleMode, int SeedIdleLimit, int PeerLimit, bool SequentialDownload);
    private sealed record FilesWantedParams(string[] Ids, int[]? FilesWanted, int[]? FilesUnwanted);
    private sealed record FilePriorityParams(string[] Ids, int[]? PriorityLow, int[]? PriorityNormal, int[]? PriorityHigh);
    private sealed record RemoveParams(string[] Ids, bool DeleteLocalData);
    private sealed record FieldsParams(string[] Fields);
    // Null arrays are left out of the request. Never send an empty one: the protocol reads that as "all files".
    private sealed record AddParams(
        string? Filename, string? Metainfo, string? DownloadDir, bool Paused,
        int[]? FilesUnwanted = null, int[]? PriorityHigh = null, int[]? PriorityLow = null,
        bool? SequentialDownload = null);
    private sealed record FreeSpaceParams(string Path);
    private sealed record AltSpeedEnabledParams(bool AltSpeedEnabled);
    private sealed record PortTestParams(string IpProtocol);

    private static readonly string[] PreferenceFields =
    [
        "download_dir", "incomplete_dir_enabled", "incomplete_dir", "start_added_torrents", "rename_partial_files",
        "trash_original_torrent_files", "download_queue_enabled", "download_queue_size", "seed_queue_enabled",
        "seed_queue_size", "seed_ratio_limited", "seed_ratio_limit", "idle_seeding_limit_enabled", "idle_seeding_limit",
        "peer_limit_global", "peer_limit_per_torrent", "encryption", "dht_enabled", "pex_enabled", "lpd_enabled",
        "peer_port", "port_forwarding_enabled", "alt_speed_down", "alt_speed_up", "alt_speed_time_enabled",
        "alt_speed_time_begin", "alt_speed_time_end", "alt_speed_time_day", "blocklist_enabled", "blocklist_url",
    ];

    // The server downloads the list before answering; a big list on a slow link takes minutes.
    private static readonly TimeSpan BlocklistUpdateTimeout = TimeSpan.FromMinutes(5);

    private sealed class BlocklistSizeDto
    {
        public double BlocklistSize { get; init; }
    }

    // Numbers as double: Transmission 4.1.0 and 4.1.1+ serialize some of them differently (spec section 5).
    private sealed class PreferencesDto
    {
        public string? DownloadDir { get; init; }
        public bool IncompleteDirEnabled { get; init; }
        public string? IncompleteDir { get; init; }
        public bool StartAddedTorrents { get; init; }
        public bool RenamePartialFiles { get; init; }
        public bool TrashOriginalTorrentFiles { get; init; }
        public bool DownloadQueueEnabled { get; init; }
        public double DownloadQueueSize { get; init; }
        public bool SeedQueueEnabled { get; init; }
        public double SeedQueueSize { get; init; }
        public bool SeedRatioLimited { get; init; }
        public double SeedRatioLimit { get; init; }
        public bool IdleSeedingLimitEnabled { get; init; }
        public double IdleSeedingLimit { get; init; }
        public double PeerLimitGlobal { get; init; }
        public double PeerLimitPerTorrent { get; init; }
        public string? Encryption { get; init; }
        public bool DhtEnabled { get; init; }
        public bool PexEnabled { get; init; }
        public bool LpdEnabled { get; init; }
        public double PeerPort { get; init; }
        public bool PortForwardingEnabled { get; init; }
        public double AltSpeedDown { get; init; }
        public double AltSpeedUp { get; init; }
        public bool AltSpeedTimeEnabled { get; init; }
        public double AltSpeedTimeBegin { get; init; }
        public double AltSpeedTimeEnd { get; init; }
        public double AltSpeedTimeDay { get; init; }
        public bool BlocklistEnabled { get; init; }
        public string? BlocklistUrl { get; init; }
    }

    private sealed record PreferencesParams(
        string DownloadDir, bool IncompleteDirEnabled, string IncompleteDir, bool StartAddedTorrents,
        bool RenamePartialFiles, bool TrashOriginalTorrentFiles, bool DownloadQueueEnabled, int DownloadQueueSize,
        bool SeedQueueEnabled, int SeedQueueSize, bool SeedRatioLimited, double SeedRatioLimit,
        bool IdleSeedingLimitEnabled, int IdleSeedingLimit, int PeerLimitGlobal, int PeerLimitPerTorrent,
        string Encryption, bool DhtEnabled, bool PexEnabled, bool LpdEnabled, int PeerPort, bool PortForwardingEnabled,
        int AltSpeedDown, int AltSpeedUp, bool AltSpeedTimeEnabled, int AltSpeedTimeBegin, int AltSpeedTimeEnd,
        int AltSpeedTimeDay, bool BlocklistEnabled, string BlocklistUrl);

    private sealed class PortTestDto
    {
        public bool PortIsOpen { get; init; }
    }

    private sealed record SpeedLimitsSetParams(
        int SpeedLimitDown, bool SpeedLimitDownEnabled, int SpeedLimitUp, bool SpeedLimitUpEnabled, bool AltSpeedEnabled);

    private sealed class TorrentGetResult
    {
        public List<TorrentDto>? Torrents { get; init; }
        public List<int>? Removed { get; init; }
    }

    private sealed class TorrentDto
    {
        public int Id { get; init; }
        public string? HashString { get; init; }
        public string? Name { get; init; }
        public int Status { get; init; }
        public double PercentDone { get; init; }
        public long TotalSize { get; init; }
        public long RateDownload { get; init; }
        public long RateUpload { get; init; }
        public long Eta { get; init; }
        public double UploadRatio { get; init; }
        public int Error { get; init; }
        public string? ErrorString { get; init; }
        public bool IsStalled { get; init; }
        public long AddedDate { get; init; }
        public int QueuePosition { get; init; }
        public List<string>? Labels { get; init; }
    }

    private sealed class DetailGetResult
    {
        public List<DetailDto>? Torrents { get; init; }
    }

    private sealed class DetailDto
    {
        public string? HashString { get; init; }
        public string? Name { get; init; }
        public int Status { get; init; }
        public bool IsStalled { get; init; }
        public double PercentDone { get; init; }
        public int Error { get; init; }
        public string? ErrorString { get; init; }
        public string? DownloadDir { get; init; }
        public long TotalSize { get; init; }
        public long SizeWhenDone { get; init; }
        public long LeftUntilDone { get; init; }
        public long HaveValid { get; init; }
        public long DownloadedEver { get; init; }
        public long UploadedEver { get; init; }
        public long CorruptEver { get; init; }
        public double UploadRatio { get; init; }
        public long RateDownload { get; init; }
        public long RateUpload { get; init; }
        public long Eta { get; init; }
        public bool DownloadLimited { get; init; }
        public double DownloadLimit { get; init; }
        public bool UploadLimited { get; init; }
        public double UploadLimit { get; init; }
        public int PeersConnected { get; init; }
        public int PeersSendingToUs { get; init; }
        public int PeersGettingFromUs { get; init; }
        public int PeerLimit { get; init; }
        public bool SequentialDownload { get; init; }
        public long AddedDate { get; init; }
        public long DoneDate { get; init; }
        public long ActivityDate { get; init; }
        public long DateCreated { get; init; }
        public string? Creator { get; init; }
        public string? Comment { get; init; }
        public int PieceCount { get; init; }
        public long PieceSize { get; init; }
        public bool IsPrivate { get; init; }
        public string? MagnetLink { get; init; }
        public double MetadataPercentComplete { get; init; }
        public bool HonorsSessionLimits { get; init; }
        public int BandwidthPriority { get; init; }
        public int SeedRatioMode { get; init; }
        public double SeedRatioLimit { get; init; }
        public int SeedIdleMode { get; init; }
        public int SeedIdleLimit { get; init; }
        public List<string>? Labels { get; init; }
        public List<TrackerStatDto>? TrackerStats { get; init; }
        public List<PeerDto>? Peers { get; init; }
        public List<FileDto>? Files { get; init; }
        public List<FileStatDto>? FileStats { get; init; }
    }

    private sealed class FileDto
    {
        public string? Name { get; init; }
        public long Length { get; init; }
    }

    private sealed class FileStatDto
    {
        public long BytesCompleted { get; init; }
        public bool Wanted { get; init; }
        public int Priority { get; init; }
    }

    private sealed class TrackerStatDto
    {
        public int Id { get; init; }
        public int Tier { get; init; }
        public string? Announce { get; init; }
        public string? Host { get; init; }
        public bool IsBackup { get; init; }
        public int AnnounceState { get; init; }
        public bool HasAnnounced { get; init; }
        public bool LastAnnounceSucceeded { get; init; }
        public bool LastAnnounceTimedOut { get; init; }
        public string? LastAnnounceResult { get; init; }
        public long NextAnnounceTime { get; init; }
        public int SeederCount { get; init; }
        public int LeecherCount { get; init; }
        public int DownloadCount { get; init; }
        public int LastAnnouncePeerCount { get; init; }
    }

    private sealed class PeerDto
    {
        public string? Address { get; init; }
        public int Port { get; init; }
        public string? ClientName { get; init; }
        public string? FlagStr { get; init; }
        public double Progress { get; init; }
        public long RateToClient { get; init; }
        public long RateToPeer { get; init; }
        public bool IsEncrypted { get; init; }
        public bool IsIncoming { get; init; }
        public bool IsUtp { get; init; }
    }

    private sealed class AddResultDto
    {
        public AddedTorrentDto? TorrentAdded { get; init; }
        public AddedTorrentDto? TorrentDuplicate { get; init; }
    }

    private sealed class AddedTorrentDto
    {
        public string? HashString { get; init; }
        public string? Name { get; init; }
    }

    private sealed class DownloadDirDto
    {
        public string? DownloadDir { get; init; }
    }

    private sealed class AltSpeedDto
    {
        public bool AltSpeedEnabled { get; init; }
        public double AltSpeedDown { get; init; }
        public double AltSpeedUp { get; init; }
    }

    private sealed class FreeSpaceDto
    {
        public string? Path { get; init; }
        public long SizeBytes { get; init; }
        public long TotalSize { get; init; }
    }

    private sealed class SessionStatsDto
    {
        public int TorrentCount { get; init; }
        public int ActiveTorrentCount { get; init; }
        public int PausedTorrentCount { get; init; }
        public long DownloadSpeed { get; init; }
        public long UploadSpeed { get; init; }
        public StatsTotalsDto? CurrentStats { get; init; }
        public StatsTotalsDto? CumulativeStats { get; init; }
    }

    private sealed class StatsTotalsDto
    {
        public long DownloadedBytes { get; init; }
        public long UploadedBytes { get; init; }
        public long FilesAdded { get; init; }
        public long SecondsActive { get; init; }
        public long SessionCount { get; init; }
    }

    // double, not int: Transmission 4.1.0 serialized these differently than 4.1.1+ (spec section 5).
    private sealed class SpeedLimitsDto
    {
        public double SpeedLimitDown { get; init; }
        public bool SpeedLimitDownEnabled { get; init; }
        public double SpeedLimitUp { get; init; }
        public bool SpeedLimitUpEnabled { get; init; }
        public bool AltSpeedEnabled { get; init; }
    }

    public async Task<TorrentUpdate> GetTorrentsAsync(bool recentlyActiveOnly, CancellationToken ct = default)
    {
        // Spec 3.1: ids may be the string "recently_active"; omitting ids means all torrents.
        var parameters = new TorrentGetParams(TorrentFields, recentlyActiveOnly ? "recently_active" : null);

        var result = await connection.Transport
            .CallAsync<TorrentGetParams, TorrentGetResult>("torrent_get", parameters, ct)
            .ConfigureAwait(false);

        var torrents = (result.Torrents ?? []).Select(Map).ToList();
        return new TorrentUpdate(torrents, result.Removed ?? [], IsFullRefresh: !recentlyActiveOnly);
    }

    public async Task<TorrentUpdate> GetTorrentsAsync(IReadOnlyCollection<string> hashes, CancellationToken ct = default)
    {
        if (hashes.Count == 0) return new TorrentUpdate([], [], IsFullRefresh: false);

        var result = await connection.Transport
            .CallAsync<TorrentGetParams, TorrentGetResult>("torrent_get", new TorrentGetParams(TorrentFields, hashes.ToArray()), ct)
            .ConfigureAwait(false);

        return new TorrentUpdate((result.Torrents ?? []).Select(Map).ToList(), [], IsFullRefresh: false);
    }

    public async Task<SessionStats> GetSessionStatsAsync(CancellationToken ct = default)
    {
        var dto = await connection.Transport
            .CallAsync<object, SessionStatsDto>("session_stats", null, ct)
            .ConfigureAwait(false);

        return new SessionStats(dto.TorrentCount, dto.ActiveTorrentCount, dto.PausedTorrentCount,
            dto.DownloadSpeed, dto.UploadSpeed, Totals(dto.CurrentStats), Totals(dto.CumulativeStats));
    }

    public async Task<TorrentDetails?> GetTorrentDetailsAsync(string hash, DetailFiles files = DetailFiles.None, CancellationToken ct = default)
    {
        string[] fields = files switch
        {
            DetailFiles.NamesAndProgress => [..DetailFields, "files", "file_stats"],
            DetailFiles.ProgressOnly => [..DetailFields, "file_stats"],
            _ => DetailFields,
        };
        var result = await connection.Transport
            .CallAsync<DetailGetParams, DetailGetResult>("torrent_get", new DetailGetParams(fields, [hash]), ct)
            .ConfigureAwait(false);

        if (result.Torrents is not [var dto, ..]) return null;

        var details = MapDetails(dto);
        return files switch
        {
            DetailFiles.NamesAndProgress => details with { Files = MapFiles(dto) },
            DetailFiles.ProgressOnly => details with { FileProgress = (dto.FileStats ?? []).Select(MapProgress).ToList() },
            _ => details,
        };
    }

    private static readonly string[] FileListFields = ["hash_string", "name", "metadata_percent_complete", "files", "file_stats"];

    public async Task<TorrentFileList?> GetFileListAsync(string hash, CancellationToken ct = default)
    {
        var result = await connection.Transport
            .CallAsync<DetailGetParams, DetailGetResult>("torrent_get", new DetailGetParams(FileListFields, [hash]), ct)
            .ConfigureAwait(false);

        return result.Torrents is [var dto, ..]
            ? new TorrentFileList(dto.Name ?? "", dto.MetadataPercentComplete, MapFiles(dto))
            : null;
    }

    // Unlike the file lists, an empty labels array means "no labels", not "all".
    public Task SetLabelsAsync(IReadOnlyList<string> hashes, IReadOnlyList<string> labels, CancellationToken ct = default)
        => hashes.Count == 0
            ? Task.CompletedTask
            : SendAsync("torrent_set", new LabelsParams(hashes.ToArray(), labels.ToArray()), ct);

    // tracker_list: one announce URL per line, a blank line between tiers.
    public Task SetTrackersAsync(string hash, IReadOnlyList<IReadOnlyList<string>> tiers, CancellationToken ct = default)
        => SendAsync("torrent_set", new TrackerListParams([hash], FormatTrackerList(tiers)), ct);

    internal static string FormatTrackerList(IReadOnlyList<IReadOnlyList<string>> tiers)
        => string.Join("\n\n", tiers.Where(t => t.Count > 0).Select(t => string.Join('\n', t)));

    // The server takes exactly one torrent here.
    public Task RenamePathAsync(string hash, string path, string newName, CancellationToken ct = default)
        => SendAsync("torrent_rename_path", new RenamePathParams([hash], path, newName), ct);

    public Task SetTorrentOptionsAsync(string hash, TorrentOptions o, CancellationToken ct = default)
        => SendAsync("torrent_set", new TorrentOptionsParams(
            [hash], o.DownloadLimited, o.DownloadLimitKilobytes, o.UploadLimited, o.UploadLimitKilobytes,
            o.HonorsSessionLimits, (int)o.Priority, (int)o.RatioMode, o.RatioLimit,
            (int)o.IdleMode, o.IdleLimitMinutes, o.PeerLimit, o.SequentialDownload), ct);

    public Task SetLocationAsync(IReadOnlyList<string> hashes, string location, bool moveFiles, CancellationToken ct = default)
        => hashes.Count == 0
            ? Task.CompletedTask
            : SendAsync("torrent_set_location", new SetLocationParams(hashes.ToArray(), location, moveFiles), ct);

    // Spec 3.2: an empty index array means "all files", so an empty selection must never be sent.
    public Task SetFilesWantedAsync(string hash, IReadOnlyList<int> fileIndices, bool wanted, CancellationToken ct = default)
    {
        if (fileIndices.Count == 0) return Task.CompletedTask;

        var indices = fileIndices.ToArray();
        return SendAsync("torrent_set", new FilesWantedParams([hash], wanted ? indices : null, wanted ? null : indices), ct);
    }

    public Task SetFilePriorityAsync(string hash, IReadOnlyList<int> fileIndices, FilePriority priority, CancellationToken ct = default)
    {
        if (fileIndices.Count == 0) return Task.CompletedTask;

        var indices = fileIndices.ToArray();
        return SendAsync("torrent_set", new FilePriorityParams([hash],
            priority == FilePriority.Low ? indices : null,
            priority == FilePriority.Normal ? indices : null,
            priority == FilePriority.High ? indices : null), ct);
    }

    public Task StartAsync(IReadOnlyList<string> hashes, bool startNow, CancellationToken ct = default)
        => hashes.Count == 0
            ? Task.CompletedTask
            : SendAsync(startNow ? "torrent_start_now" : "torrent_start", new IdsParams(hashes.ToArray()), ct);

    public Task StopAsync(IReadOnlyList<string> hashes, CancellationToken ct = default)
        => hashes.Count == 0
            ? Task.CompletedTask
            : SendAsync("torrent_stop", new IdsParams(hashes.ToArray()), ct);

    public Task VerifyAsync(IReadOnlyList<string> hashes, CancellationToken ct = default)
        => hashes.Count == 0 ? Task.CompletedTask : SendAsync("torrent_verify", new IdsParams(hashes.ToArray()), ct);

    public Task ReannounceAsync(IReadOnlyList<string> hashes, CancellationToken ct = default)
        => hashes.Count == 0 ? Task.CompletedTask : SendAsync("torrent_reannounce", new IdsParams(hashes.ToArray()), ct);

    public Task RemoveAsync(IReadOnlyList<string> hashes, bool deleteLocalData, CancellationToken ct = default)
        => hashes.Count == 0
            ? Task.CompletedTask
            : SendAsync("torrent_remove", new RemoveParams(hashes.ToArray(), deleteLocalData), ct);

    public Task MoveInQueueAsync(IReadOnlyList<string> hashes, QueueMove move, CancellationToken ct = default)
    {
        if (hashes.Count == 0) return Task.CompletedTask;

        var method = move switch
        {
            QueueMove.Top => "queue_move_top",
            QueueMove.Up => "queue_move_up",
            QueueMove.Down => "queue_move_down",
            QueueMove.Bottom => "queue_move_bottom",
            _ => throw new ArgumentOutOfRangeException(nameof(move), move, null),
        };
        return SendAsync(method, new IdsParams(hashes.ToArray()), ct);
    }

    public async Task<SpeedLimits> GetSpeedLimitsAsync(CancellationToken ct = default)
    {
        var dto = await connection.Transport
            .CallAsync<FieldsParams, SpeedLimitsDto>("session_get", new FieldsParams(SpeedLimitFields), ct)
            .ConfigureAwait(false);

        return new SpeedLimits(
            dto.SpeedLimitDownEnabled, (int)Math.Round(dto.SpeedLimitDown),
            dto.SpeedLimitUpEnabled, (int)Math.Round(dto.SpeedLimitUp),
            dto.AltSpeedEnabled);
    }

    public Task SetSpeedLimitsAsync(SpeedLimits limits, CancellationToken ct = default)
        => SendAsync("session_set", new SpeedLimitsSetParams(
            limits.DownloadKilobytesPerSecond, limits.DownloadLimited,
            limits.UploadKilobytesPerSecond, limits.UploadLimited,
            limits.AlternativeSpeedsEnabled), ct);

    public async Task<AltSpeed> GetAltSpeedAsync(CancellationToken ct = default)
    {
        var dto = await connection.Transport
            .CallAsync<FieldsParams, AltSpeedDto>("session_get",
                new FieldsParams(["alt_speed_enabled", "alt_speed_down", "alt_speed_up"]), ct)
            .ConfigureAwait(false);

        return new AltSpeed(dto.AltSpeedEnabled, (int)Math.Round(dto.AltSpeedDown), (int)Math.Round(dto.AltSpeedUp));
    }

    public Task SetAltSpeedEnabledAsync(bool enabled, CancellationToken ct = default)
        => SendAsync("session_set", new AltSpeedEnabledParams(enabled), ct);

    public async Task<Synchro.Core.Servers.ServerPreferences> GetServerPreferencesAsync(CancellationToken ct = default)
    {
        var d = await connection.Transport
            .CallAsync<FieldsParams, PreferencesDto>("session_get", new FieldsParams(PreferenceFields), ct)
            .ConfigureAwait(false);

        static int I(double value) => (int)Math.Round(value);
        return new Synchro.Core.Servers.ServerPreferences(
            d.DownloadDir ?? "", d.IncompleteDirEnabled, d.IncompleteDir ?? "", d.StartAddedTorrents,
            d.RenamePartialFiles, d.TrashOriginalTorrentFiles, d.DownloadQueueEnabled, I(d.DownloadQueueSize),
            d.SeedQueueEnabled, I(d.SeedQueueSize), d.SeedRatioLimited, d.SeedRatioLimit,
            d.IdleSeedingLimitEnabled, I(d.IdleSeedingLimit), I(d.PeerLimitGlobal), I(d.PeerLimitPerTorrent),
            d.Encryption switch
            {
                "required" => Synchro.Core.Servers.EncryptionMode.Required,
                "allowed" or "tolerated" => Synchro.Core.Servers.EncryptionMode.Allowed,
                _ => Synchro.Core.Servers.EncryptionMode.Preferred,
            },
            d.DhtEnabled, d.PexEnabled, d.LpdEnabled, I(d.PeerPort), d.PortForwardingEnabled,
            I(d.AltSpeedDown), I(d.AltSpeedUp), d.AltSpeedTimeEnabled, I(d.AltSpeedTimeBegin), I(d.AltSpeedTimeEnd),
            I(d.AltSpeedTimeDay), d.BlocklistEnabled, d.BlocklistUrl ?? "");
    }

    public Task SetServerPreferencesAsync(Synchro.Core.Servers.ServerPreferences p, CancellationToken ct = default)
        => SendAsync("session_set", new PreferencesParams(
            p.DownloadDirectory, p.IncompleteDirectoryEnabled, p.IncompleteDirectory, p.StartAddedTorrents,
            p.RenamePartialFiles, p.TrashOriginalTorrentFiles, p.DownloadQueueEnabled, p.DownloadQueueSize,
            p.SeedQueueEnabled, p.SeedQueueSize, p.SeedRatioLimited, p.SeedRatioLimit,
            p.IdleSeedingLimitEnabled, p.IdleSeedingLimitMinutes, p.PeerLimitGlobal, p.PeerLimitPerTorrent,
            p.Encryption.ToString().ToLowerInvariant(), p.DhtEnabled, p.PexEnabled, p.LpdEnabled, p.PeerPort,
            p.PortForwardingEnabled, p.AltSpeedDownKilobytes, p.AltSpeedUpKilobytes, p.AltSpeedTimeEnabled,
            p.AltSpeedTimeBegin, p.AltSpeedTimeEnd, p.AltSpeedTimeDays, p.BlocklistEnabled, p.BlocklistUrl), ct);

    public async Task<int> GetBlocklistSizeAsync(CancellationToken ct = default)
        => (int)(await connection.Transport
            .CallAsync<FieldsParams, BlocklistSizeDto>("session_get", new FieldsParams(["blocklist_size"]), ct)
            .ConfigureAwait(false)).BlocklistSize;

    public async Task<int> UpdateBlocklistAsync(CancellationToken ct = default)
        => (int)(await connection.Transport
            .CallAsync<object, BlocklistSizeDto>("blocklist_update", null, BlocklistUpdateTimeout, ct)
            .ConfigureAwait(false)).BlocklistSize;

    public async Task<bool> TestPortAsync(string ipProtocol, CancellationToken ct = default)
        => (await connection.Transport
            .CallAsync<PortTestParams, PortTestDto>("port_test", new PortTestParams(ipProtocol), ct)
            .ConfigureAwait(false)).PortIsOpen;

    public async Task<AddTorrentResult> AddTorrentAsync(AddTorrentRequest request, CancellationToken ct = default)
    {
        // Magnet links and URLs go in `filename`; the server fetches them. A local .torrent file
        // is sent as base64 in `metainfo` (spec 3.4).
        var parameters = request.Source switch
        {
            AddTorrentSource.Magnet magnet => new AddParams(magnet.Link, null, request.DownloadDirectory, request.Paused),
            AddTorrentSource.Url url => new AddParams(url.Address.ToString(), null, request.DownloadDirectory, request.Paused),
            AddTorrentSource.TorrentFile file => new AddParams(null, Convert.ToBase64String(file.Content), request.DownloadDirectory, request.Paused),
            _ => throw new ArgumentException("Unknown torrent source.", nameof(request)),
        };

        // Only sent when asked for, so the server's own default applies otherwise.
        if (request.SequentialDownload)
            parameters = parameters with { SequentialDownload = true };

        if (request.Files is { } files)
        {
            parameters = parameters with
            {
                FilesUnwanted = NonEmpty(files.Unwanted),
                PriorityHigh = NonEmpty(files.HighPriority),
                PriorityLow = NonEmpty(files.LowPriority),
            };
        }

        var result = await connection.Transport
            .CallAsync<AddParams, AddResultDto>("torrent_add", parameters, ct)
            .ConfigureAwait(false);

        // A duplicate is still a successful call: the server answers with `torrent_duplicate` instead.
        if (result.TorrentAdded is { } added)
            return new AddTorrentResult(AddTorrentOutcome.Added, added.HashString ?? "", added.Name ?? "");

        if (result.TorrentDuplicate is { } duplicate)
            return new AddTorrentResult(AddTorrentOutcome.Duplicate, duplicate.HashString ?? "", duplicate.Name ?? "");

        throw new RpcProtocolException("The server did not say whether the torrent was added.");
    }

    public async Task<string> GetDefaultDownloadDirectoryAsync(CancellationToken ct = default)
    {
        var dto = await connection.Transport
            .CallAsync<FieldsParams, DownloadDirDto>("session_get", new FieldsParams(["download_dir"]), ct)
            .ConfigureAwait(false);

        return dto.DownloadDir ?? "";
    }

    public async Task<long> GetFreeSpaceAsync(string path, CancellationToken ct = default)
        => (await GetStorageAsync(path, ct).ConfigureAwait(false)).FreeBytes;

    public async Task<StorageInfo> GetStorageAsync(string path, CancellationToken ct = default)
    {
        var dto = await connection.Transport
            .CallAsync<FreeSpaceParams, FreeSpaceDto>("free_space", new FreeSpaceParams(path), ct)
            .ConfigureAwait(false);

        // total_size is negative or missing when the server can't tell; treat that as unknown.
        return new StorageInfo(dto.Path ?? path, Math.Max(dto.SizeBytes, 0), Math.Max(dto.TotalSize, 0));
    }

    public void Dispose() => connection.Dispose();

    private Task SendAsync<TParams>(string method, TParams parameters, CancellationToken ct)
        => connection.Transport.CallAsync<TParams, EmptyResult>(method, parameters, ct);

    private static TorrentDetails MapDetails(DetailDto d) => new(
        d.HashString ?? "",
        d.Name ?? "",
        MapStatus(d.Status),
        d.IsStalled,
        d.PercentDone,
        d.Error,
        d.ErrorString,
        d.DownloadDir ?? "",
        d.TotalSize,
        d.SizeWhenDone,
        d.LeftUntilDone,
        d.HaveValid,
        d.DownloadedEver,
        d.UploadedEver,
        d.CorruptEver,
        d.UploadRatio,
        d.RateDownload,
        d.RateUpload,
        d.Eta,
        d.DownloadLimited,
        (int)Math.Round(d.DownloadLimit),
        d.UploadLimited,
        (int)Math.Round(d.UploadLimit),
        d.PeersConnected,
        d.PeersSendingToUs,
        d.PeersGettingFromUs,
        d.PeerLimit,
        Date(d.AddedDate),
        Date(d.DoneDate),
        Date(d.ActivityDate),
        Date(d.DateCreated),
        d.Creator,
        d.Comment,
        d.PieceCount,
        d.PieceSize,
        d.IsPrivate,
        d.MagnetLink,
        (d.TrackerStats ?? []).Select(t => new TrackerInfo(
            t.Id, t.Tier, t.Announce ?? "", t.Host ?? "", t.IsBackup,
            Enum.IsDefined((TrackerAnnounceState)t.AnnounceState) ? (TrackerAnnounceState)t.AnnounceState : TrackerAnnounceState.Inactive,
            t.HasAnnounced, t.LastAnnounceSucceeded, t.LastAnnounceTimedOut, t.LastAnnounceResult,
            Date(t.NextAnnounceTime), t.SeederCount, t.LeecherCount, t.DownloadCount, t.LastAnnouncePeerCount)).ToList(),
        (d.Peers ?? []).Select(p => new PeerInfo(
            p.Address ?? "", p.Port, p.ClientName ?? "", p.FlagStr ?? "", p.Progress,
            p.RateToClient, p.RateToPeer, p.IsEncrypted, p.IsIncoming, p.IsUtp)).ToList(),
        Options: new TorrentOptions(
            d.DownloadLimited, (int)Math.Round(d.DownloadLimit),
            d.UploadLimited, (int)Math.Round(d.UploadLimit),
            d.HonorsSessionLimits,
            Enum.IsDefined((BandwidthPriority)d.BandwidthPriority) ? (BandwidthPriority)d.BandwidthPriority : BandwidthPriority.Normal,
            MapSeedMode(d.SeedRatioMode), d.SeedRatioLimit,
            MapSeedMode(d.SeedIdleMode), d.SeedIdleLimit,
            d.PeerLimit,
            d.SequentialDownload),
        Labels: d.Labels ?? []);

    private static SeedLimitMode MapSeedMode(int mode) =>
        Enum.IsDefined((SeedLimitMode)mode) ? (SeedLimitMode)mode : SeedLimitMode.ServerDefault;

    // `files` and `file_stats` are parallel arrays in torrent order; the position is the file index.
    private static TorrentFileProgress MapProgress(FileStatDto stat) => new(
        stat.BytesCompleted, stat.Wanted,
        Enum.IsDefined((FilePriority)stat.Priority) ? (FilePriority)stat.Priority : FilePriority.Normal);

    private static List<TorrentFile> MapFiles(DetailDto d)
    {
        var files = d.Files ?? [];
        var stats = d.FileStats ?? [];
        return files.Select((f, i) =>
        {
            var stat = i < stats.Count ? stats[i] : null;
            var priority = stat is null || !Enum.IsDefined((FilePriority)stat.Priority) ? FilePriority.Normal : (FilePriority)stat.Priority;
            return new TorrentFile(i, f.Name ?? "", f.Length, stat?.BytesCompleted ?? 0, stat?.Wanted ?? true, priority);
        }).ToList();
    }

    private static int[]? NonEmpty(IReadOnlyList<int> indices) => indices.Count == 0 ? null : indices.ToArray();

    private static StatsTotals? Totals(StatsTotalsDto? t) =>
        t is null ? null : new StatsTotals(t.DownloadedBytes, t.UploadedBytes, t.FilesAdded, t.SecondsActive, t.SessionCount);

    // The protocol uses 0 for "never" in its date fields.
    private static DateTimeOffset? Date(long unixSeconds) =>
        unixSeconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds) : null;

    private static TorrentStatus MapStatus(int status) =>
        Enum.IsDefined((TorrentStatus)status) ? (TorrentStatus)status : TorrentStatus.Stopped;

    private static Torrent Map(TorrentDto d) => new(
        d.Id,
        d.HashString ?? "",
        d.Name ?? "",
        MapStatus(d.Status),
        d.PercentDone,
        d.TotalSize,
        d.RateDownload,
        d.RateUpload,
        d.Eta,
        d.UploadRatio,
        d.Error,
        d.ErrorString,
        d.IsStalled,
        DateTimeOffset.FromUnixTimeSeconds(d.AddedDate),
        d.QueuePosition,
        d.Labels ?? []);
}
