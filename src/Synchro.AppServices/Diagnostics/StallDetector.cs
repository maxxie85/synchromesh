using System.Diagnostics;

namespace Synchro.AppServices.Diagnostics;

/// <summary>
/// Notices when the UI thread is too busy to respond. A background thread posts a tiny job to the UI thread every
/// <c>interval</c> and measures how long it waits to run; a wait over <c>threshold</c> is a stall. While a probe is still
/// waiting no new one is posted, so one long stall is reported once, with its whole length. <c>onStall</c> runs on the
/// UI thread, right after the stall ends.
/// </summary>
public sealed class StallDetector(Action<Action> postToUi, TimeSpan threshold, Action<TimeSpan> onStall, TimeSpan interval)
    : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _stop;
    private int _probePending;

    public bool IsRunning
    {
        get { lock (_gate) return _stop is not null; }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_stop is not null) return;
            var stop = new CancellationTokenSource();
            _stop = stop;
            new Thread(() => Run(stop)) { IsBackground = true, Name = "UI stall detector" }.Start();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            // Only cancelled here: the detector thread may be waiting on it, and disposes it when it ends.
            _stop?.Cancel();
            _stop = null;
        }
    }

    public void Dispose() => Stop();

    private void Run(CancellationTokenSource stop)
    {
        var token = stop.Token;
        using var _ = stop;
        while (!token.WaitHandle.WaitOne(interval))
        {
            if (Interlocked.CompareExchange(ref _probePending, 1, 0) != 0) continue; // the last probe is still waiting

            var posted = Stopwatch.GetTimestamp();
            postToUi(() =>
            {
                var waited = Stopwatch.GetElapsedTime(posted);
                Volatile.Write(ref _probePending, 0);
                if (waited > threshold && !token.IsCancellationRequested)
                    onStall(waited);
            });
        }
    }
}
