using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Synchro.AppServices.Diagnostics;
using Synchro.ViewModels.Services;

namespace Synchro.Desktop.Services;

/// <summary>
/// Help > Log UI stalls: writes each moment the UI thread was blocked for more than 100 ms to a log file, with the
/// click or key press just before it, so a freeze can be traced to what triggered it. Off unless the user turns it on.
/// </summary>
public sealed class UiStallMonitor : IStallLogging, IDisposable
{
    private static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan BreadcrumbWindow = TimeSpan.FromSeconds(2);

    private readonly StallLog _log;
    private readonly StallDetector _detector;
    private (DateTimeOffset At, string What)? _lastAction;
    private bool _listening;

    public UiStallMonitor(string logPath)
    {
        _log = new StallLog(logPath);
        _detector = new StallDetector(a => Dispatcher.UIThread.Post(a), Threshold, OnStall, TimeSpan.FromMilliseconds(50));
    }

    public bool IsEnabled => _detector.IsRunning;

    public void SetEnabled(bool enabled)
    {
        if (enabled == IsEnabled) return;
        if (!enabled)
        {
            _detector.Stop();
            return;
        }

        ListenForInput();
        _log.Begin($"Synchromesh {AppInfo.Version}, {RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}, " +
                   $".NET {Environment.Version}; stalls over {Threshold.TotalMilliseconds:F0} ms");
        _detector.Start();
    }

    public async Task OpenLogFolderAsync()
    {
        var folder = Path.GetDirectoryName(_log.Path)!;
        Directory.CreateDirectory(folder);
        await WindowLocator.Active().Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
    }

    public void Dispose() => _detector.Dispose();

    // Runs on the UI thread, right after the stall.
    private void OnStall(TimeSpan length)
    {
        var now = DateTimeOffset.Now;
        var start = now - length;
        string? context = null;
        if (_lastAction is { } action && start - action.At < BreadcrumbWindow)
        {
            var gap = (start - action.At).TotalMilliseconds;
            context = gap >= 0 ? $"{action.What} ({gap:F0} ms before)" : action.What;
        }
        _log.Stall(start, length, context);
    }

    // Class handlers see every click and key press in every window, also ones a control already handled.
    private void ListenForInput()
    {
        if (_listening) return;
        _listening = true;
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>(
            (_, e) => Remember("click on", e.Source), RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>(
            (_, e) => Remember($"key {e.KeyModifiers}+{e.Key} on".Replace("None+", ""), e.Source),
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void Remember(string what, object? source)
    {
        if (IsEnabled) _lastAction = (DateTimeOffset.Now, $"{what} {Describe(source as Visual)}");
    }

    /// <summary>"Button "Queue order" in MainWindow": the nearest control a user would name, and its window.</summary>
    internal static string Describe(Visual? source)
    {
        if (source is null) return "nothing";
        var window = TopLevel.GetTopLevel(source)?.GetType().Name ?? "a popup";
        for (Visual? v = source; v is not null; v = v.GetVisualParent())
        {
            if (v is not (Button or MenuItem or ToggleButton or ToggleSwitch or ListBoxItem or TabItem or TextBox or ComboBox)) continue;
            var label = ToolTip.GetTip((Control)v) as string
                        ?? (v as HeaderedSelectingItemsControl)?.Header as string
                        ?? (v as MenuItem)?.Header as string
                        ?? (v as ContentControl)?.Content as string
                        ?? (v as TabItem)?.Header as string
                        ?? (v as Control)?.Name;
            return label is null ? $"{v.GetType().Name} in {window}" : $"{v.GetType().Name} \"{label}\" in {window}";
        }
        return $"{source.GetType().Name} in {window}";
    }
}
