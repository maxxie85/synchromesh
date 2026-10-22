using Synchro.Core.Torrents;

namespace Synchro.AppServices.Torrents;

/// <summary>
/// Polls the server: one full fetch first, then cheap "recently active" deltas, with a periodic
/// full refresh as a safety net (a delta only covers recently active torrents).
///
/// Events are raised on the context that called <see cref="Start"/> (the UI thread in the app),
/// because the loop deliberately does not use ConfigureAwait(false) or Task.Run.
/// </summary>
public sealed class TorrentPoller : ITorrentPoller, ITorrentDetailsFeed, IDisposable
{
    private const int FullRefreshEvery = 30; // ticks: about a minute at the default 2 s interval

    private readonly TimeSpan _interval;
    private CancellationTokenSource? _cts;
    private Task _loop = Task.CompletedTask;
    private TaskCompletionSource? _wake;
    private int _forceFullRefresh; // 0 or 1; read and cleared atomically
    private volatile DetailsRequest? _details;
    private readonly object _refreshGate = new();
    private HashSet<string> _refreshHashes = new(StringComparer.Ordinal);
    private int _fileNamesStale; // 0 or 1: set by any refresh request (an action may have renamed files)

    // The watched torrent's file names, from the last NamesAndProgress fetch; only touched by the polling loop.
    private (string Hash, IReadOnlyList<TorrentFile> Files)? _fileNames;

    private sealed record DetailsRequest(string Hash, bool IncludeFiles);

    public TorrentPoller() : this(TimeSpan.FromSeconds(2)) { }

    // For tests: lets them poll in milliseconds.
    internal TorrentPoller(TimeSpan interval) => _interval = interval;

    public event EventHandler<TorrentUpdate>? TorrentsUpdated;
    public event EventHandler<SessionStats>? StatsUpdated;
    public event EventHandler<string>? ConnectionLost;
    public event EventHandler? ConnectionRestored;
    public event EventHandler<TorrentDetailsUpdate>? DetailsUpdated;

    public void Start(ITransmissionClient client)
    {
        _cts?.Cancel();

        var cts = _cts = new CancellationTokenSource();
        _loop = RunAsync(client, cts.Token);
    }

    public void RequestRefresh()
    {
        Volatile.Write(ref _forceFullRefresh, 1);
        Volatile.Write(ref _fileNamesStale, 1);
        _wake?.TrySetResult();
    }

    public void RequestRefresh(IReadOnlyCollection<string> hashes)
    {
        if (hashes.Count == 0) return;

        lock (_refreshGate)
            _refreshHashes.UnionWith(hashes);
        Volatile.Write(ref _fileNamesStale, 1);
        _wake?.TrySetResult();
    }

    private IReadOnlyCollection<string> TakeRefreshHashes()
    {
        lock (_refreshGate)
        {
            if (_refreshHashes.Count == 0) return [];
            var taken = _refreshHashes;
            _refreshHashes = new HashSet<string>(StringComparer.Ordinal);
            return taken;
        }
    }

    public void Watch(string? hash, bool includeFiles = false)
    {
        var request = hash is null ? null : new DetailsRequest(hash, includeFiles);
        if (request == _details) return;

        _details = request;
        if (request is not null)
            _wake?.TrySetResult(); // show the new selection (or the Files tab) now, not on the next tick
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        if (cts is null) return;

        _cts = null;
        cts.Cancel();
        try { await _loop; }
        catch (OperationCanceledException) { }
        finally { cts.Dispose(); }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private async Task RunAsync(ITransmissionClient client, CancellationToken ct)
    {
        var tick = 0;
        var needFullRefresh = true;
        var lost = false;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Created before polling, so a refresh requested mid-poll skips the wait that follows.
                var wake = _wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                try
                {
                    var requested = Interlocked.Exchange(ref _forceFullRefresh, 0) == 1;
                    var full = needFullRefresh || requested || tick % FullRefreshEvery == 0;
                    var touched = TakeRefreshHashes(); // a full refresh reads them anyway
                    // File names again after an action (a rename) and with every full refresh (renames elsewhere).
                    if (Interlocked.Exchange(ref _fileNamesStale, 0) == 1 || full)
                        _fileNames = null;

                    var details = _details;

                    var torrentsTask = client.GetTorrentsAsync(recentlyActiveOnly: !full, ct);
                    var touchedTask = full || touched.Count == 0
                        ? Task.FromResult<TorrentUpdate?>(null)
                        : AsNullable(client.GetTorrentsAsync(touched, ct));
                    var statsTask = client.GetSessionStatsAsync(ct);
                    var detailsTask = details is null
                        ? Task.FromResult<TorrentDetails?>(null)
                        : client.GetTorrentDetailsAsync(details.Hash, FilesToFetch(details), ct);
                    await Task.WhenAll(torrentsTask, touchedTask, statsTask, detailsTask);

                    if (lost)
                    {
                        lost = false;
                        ConnectionRestored?.Invoke(this, EventArgs.Empty);
                    }

                    needFullRefresh = false;
                    TorrentsUpdated?.Invoke(this, Merge(torrentsTask.Result, touchedTask.Result));
                    StatsUpdated?.Invoke(this, statsTask.Result);
                    if (details is not null)
                        DetailsUpdated?.Invoke(this, new TorrentDetailsUpdate(details.Hash, WithFileNames(details, detailsTask.Result)));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Keep trying: a NAS reboot or Wi-Fi drop shouldn't need a manual reconnect.
                    // We may have missed removals meanwhile, so the next successful poll is a full one.
                    needFullRefresh = true;
                    if (!lost)
                    {
                        lost = true;
                        ConnectionLost?.Invoke(this, ex.Message);
                    }
                }

                tick++;
                await WaitForNextPollAsync(wake.Task, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped while waiting for the next poll.
        }
    }

    private static async Task<TorrentUpdate?> AsNullable(Task<TorrentUpdate> task) => await task;

    /// <summary>The delta plus the torrents an action touched; those come from their own fetch, which is newer.</summary>
    private static TorrentUpdate Merge(TorrentUpdate delta, TorrentUpdate? touched)
    {
        if (touched is null) return delta;

        var fresh = touched.Torrents.Select(t => t.HashString).ToHashSet(StringComparer.Ordinal);
        return delta with { Torrents = [..delta.Torrents.Where(t => !fresh.Contains(t.HashString)), ..touched.Torrents] };
    }

    private DetailFiles FilesToFetch(DetailsRequest details) =>
        !details.IncludeFiles ? DetailFiles.None
        : _fileNames is { } names && names.Hash == details.Hash ? DetailFiles.ProgressOnly
        : DetailFiles.NamesAndProgress;

    /// <summary>
    /// Turns a progress-only answer back into full files using the names fetched before. When the number of files
    /// differs (a magnet's metadata just arrived), the names are fetched again next time and this update carries no
    /// files, so the Files tab keeps showing the last list.
    /// </summary>
    private TorrentDetails? WithFileNames(DetailsRequest request, TorrentDetails? details)
    {
        if (details is null || !request.IncludeFiles) return details;

        if (details.Files is { } files)
        {
            _fileNames = (request.Hash, files);
            return details;
        }

        if (details.FileProgress is not { } progress) return details;

        if (_fileNames is not { } names || names.Hash != request.Hash || names.Files.Count != progress.Count)
        {
            _fileNames = null;
            return details with { FileProgress = null };
        }

        var merged = names.Files
            .Select((file, i) => file with
            {
                BytesCompleted = progress[i].BytesCompleted, Wanted = progress[i].Wanted, Priority = progress[i].Priority,
            })
            .ToList();
        return details with { Files = merged, FileProgress = null };
    }

    private async Task WaitForNextPollAsync(Task wake, CancellationToken ct)
    {
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(_interval, delayCts.Token);

        await Task.WhenAny(delay, wake);
        delayCts.Cancel(); // don't leave the timer running when a refresh request won the race

        ct.ThrowIfCancellationRequested();
    }
}
