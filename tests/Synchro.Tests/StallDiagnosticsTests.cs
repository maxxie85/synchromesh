using System.Collections.Concurrent;
using Synchro.AppServices.Diagnostics;
using Xunit;

namespace Synchro.Tests;

public sealed class StallDetectorTests : IDisposable
{
    // Stands in for the UI thread: one thread running posted jobs in order.
    private readonly BlockingCollection<Action> _queue = new();
    private readonly ConcurrentQueue<TimeSpan> _stalls = new();
    private readonly Thread _ui;

    public StallDetectorTests()
    {
        _ui = new Thread(() => { foreach (var job in _queue.GetConsumingEnumerable()) job(); }) { IsBackground = true };
        _ui.Start();
    }

    public void Dispose() => _queue.CompleteAdding();

    private StallDetector Create(int thresholdMs = 100) =>
        new(_queue.Add, TimeSpan.FromMilliseconds(thresholdMs), _stalls.Enqueue, TimeSpan.FromMilliseconds(20));

    [Fact]
    public async Task A_blocked_ui_thread_is_reported_once_with_its_length()
    {
        using var detector = Create();
        detector.Start();
        await Task.Delay(100);

        _queue.Add(() => Thread.Sleep(400)); // the UI thread is busy for 400 ms
        await TestWait.UntilAsync(() => !_stalls.IsEmpty);
        await Task.Delay(150);

        var stall = Assert.Single(_stalls);
        Assert.InRange(stall.TotalMilliseconds, 300, 1500);
    }

    [Fact]
    public async Task An_idle_ui_thread_reports_nothing()
    {
        using var detector = Create(thresholdMs: 250);
        detector.Start();

        await Task.Delay(500);

        Assert.Empty(_stalls);
    }

    [Fact]
    public async Task Nothing_is_reported_after_stopping()
    {
        using var detector = Create();
        detector.Start();
        detector.Stop();
        Assert.False(detector.IsRunning);

        _queue.Add(() => Thread.Sleep(300));
        await Task.Delay(500);

        Assert.Empty(_stalls);
    }
}

public sealed class StallLogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tm-stalllog-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Writes_a_header_and_one_line_per_stall()
    {
        var log = new StallLog(Path.Combine(_dir, "logs", "ui-stalls.log"));

        log.Begin("Synchromesh 1.2.0, macOS");
        log.Stall(new DateTimeOffset(2026, 10, 20, 19, 41, 2, 118, TimeSpan.Zero), TimeSpan.FromMilliseconds(412.4),
            "click on Button \"Queue order\" (35 ms before)");
        log.Stall(DateTimeOffset.Now, TimeSpan.FromMilliseconds(150), null);
        await log.FlushAsync();

        var lines = File.ReadAllLines(log.Path).Where(l => l.Length > 0).ToArray();
        Assert.Equal(3, lines.Length);
        Assert.Contains("Synchromesh 1.2.0, macOS", lines[0]);
        Assert.Equal("2026-10-20 19:41:02.118  UI blocked 412 ms  after click on Button \"Queue order\" (35 ms before)", lines[1]);
        Assert.EndsWith("UI blocked 150 ms", lines[2]);
    }

    [Fact]
    public async Task A_large_log_is_moved_aside_when_a_session_starts()
    {
        var path = Path.Combine(_dir, "ui-stalls.log");
        File.WriteAllText(path, new string('x', 2000));
        var log = new StallLog(path, maxBytes: 1000);

        log.Begin("new session");
        await log.FlushAsync();

        Assert.Equal(2000, new FileInfo(path + ".1").Length);
        Assert.Contains("new session", File.ReadAllText(path));
    }
}

public class StallBreadcrumbTests
{
    [Fact]
    public void A_click_is_named_after_the_control_a_user_would_recognise()
    {
        var queue = new Avalonia.Controls.Button();
        Avalonia.Controls.ToolTip.SetTip(queue, "Queue order");
        var save = new Avalonia.Controls.Button { Content = "Save" };
        var text = new Avalonia.Controls.TextBlock();

        Assert.Equal("Button \"Queue order\" in a popup", Synchro.Desktop.Services.UiStallMonitor.Describe(queue));
        Assert.Equal("Button \"Save\" in a popup", Synchro.Desktop.Services.UiStallMonitor.Describe(save));
        Assert.Equal("TextBlock in a popup", Synchro.Desktop.Services.UiStallMonitor.Describe(text));
        Assert.Equal("nothing", Synchro.Desktop.Services.UiStallMonitor.Describe(null));
    }
}
