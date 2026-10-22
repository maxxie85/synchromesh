using System.Diagnostics;
using Synchro.AppServices.Connection;
using Synchro.Core.Torrents;

namespace Synchro.ViewModels.Services;

/// <summary>
/// Holds external add requests until start-up is done and the connection has settled, then hands them to the add
/// flow one batch at a time. While a connection attempt is in progress it waits; once connected (or once it is clear
/// there is no connection) the flow takes over, which also explains "Not connected" to the user.
/// </summary>
// Everything runs on the UI thread (callers enqueue there, StatusChanged is raised there), so no locking is needed.
public sealed class ExternalAddQueue(IAddTorrentFlow flow, IConnectionService connection) : IExternalAddQueue
{
    private readonly Queue<IReadOnlyList<AddTorrentSource>> _pending = new();
    private bool _started;
    private bool _delivering;

    public void Enqueue(IReadOnlyList<AddTorrentSource> sources)
    {
        if (sources.Count == 0) return;

        _pending.Enqueue(sources);
        _ = DeliverAsync();
    }

    public void Start()
    {
        if (_started) return;

        _started = true;
        connection.StatusChanged += (_, _) => _ = DeliverAsync();
        _ = DeliverAsync();
    }

    private async Task DeliverAsync()
    {
        // One batch at a time: a magnet clicked while an Add dialog is open waits for that dialog to close.
        if (!_started || _delivering) return;

        _delivering = true;
        try
        {
            while (connection.Status.State != ConnectionState.Connecting && _pending.TryDequeue(out var batch))
            {
                try
                {
                    await flow.AddAsync(batch);
                }
                catch (Exception ex)
                {
                    // Fire-and-forget from an OS event: a failure must not take the app down or block later batches.
                    Trace.TraceError($"Adding an opened torrent failed: {ex}");
                }
            }
        }
        finally
        {
            _delivering = false;
        }
    }
}
