using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Synchro.AppServices.Diagnostics;

/// <summary>
/// Appends stall reports to a plain text file, one line each, off the UI thread and in order. A file that has grown
/// past <c>maxBytes</c> is moved to "&lt;name&gt;.1" when logging starts, so the log never grows without bound.
/// </summary>
public sealed class StallLog(string path, long maxBytes = 1_000_000)
{
    private readonly object _gate = new();
    private Task _writes = Task.CompletedTask;

    public string Path { get; } = path;

    /// <summary>Starts a session: rotates a large file and writes a header line (app, OS, time).</summary>
    public void Begin(string header)
    {
        Enqueue(() =>
        {
            var info = new FileInfo(Path);
            if (info.Exists && info.Length > maxBytes)
                File.Move(Path, Path + ".1", overwrite: true);
            return $"{Environment.NewLine}--- {Stamp(DateTimeOffset.Now)}  {header}";
        });
    }

    /// <summary>One stall: how long, and what the user did just before (if anything).</summary>
    public void Stall(DateTimeOffset at, TimeSpan length, string? lastAction)
    {
        var ms = length.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture);
        Enqueue(() => $"{Stamp(at)}  UI blocked {ms} ms" + (lastAction is null ? "" : $"  after {lastAction}"));
    }

    /// <summary>Waits until everything queued is on disk (for tests and shutdown).</summary>
    public Task FlushAsync()
    {
        lock (_gate) return _writes;
    }

    private static string Stamp(DateTimeOffset at) => at.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

    private void Enqueue(Func<string> line)
    {
        lock (_gate)
        {
            _writes = _writes.ContinueWith(_ =>
            {
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                    File.AppendAllText(Path, line() + Environment.NewLine, Encoding.UTF8);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceWarning($"Couldn't write the stall log: {ex.Message}");
                }
            }, TaskScheduler.Default);
        }
    }
}
