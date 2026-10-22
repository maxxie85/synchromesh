using Avalonia;
using Synchro.Desktop.Services;

namespace Synchro.Desktop;

internal static class Program
{
    public const string AppId = "nl.vinch.synchromesh";

    [STAThread]
    public static void Main(string[] args)
    {
        // Windows and Linux desktops start a new process for every .torrent or magnet: link they open; hand those to
        // the running copy. macOS sends them to the running app itself (LaunchServices).
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            SingleInstance instance = new(AppId);
            if (instance.TryClaim())
                App.SingleInstance = instance;
            else if (instance.TryForward(args, TimeSpan.FromSeconds(3)))
                return;
            // Otherwise the running copy didn't answer (it may be closing): start normally.
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Public: the Avalonia XAML previewer (Rider, VS Code) finds this method by reflection.
    // ReSharper disable once MemberCanBePrivate.Global
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // No Services submenu in the app menu; App.axaml supplies the other standard items instead.
            .With(new MacOSPlatformOptions { DisableDefaultApplicationMenuItems = true })
            // macOS: OpenGL, not Avalonia's default Metal. With Metal, opening a menu or dialog from a click often froze
            // the UI for about a second: -[CAMetalLayer nextDrawable] waited for a drawable until its 1 s timeout
            // (found with the UI stall log and `sample`, 2026-10-20). SYNCHROMESH_RENDERER=metal brings Metal back to
            // check a newer Avalonia.
            .With(new AvaloniaNativePlatformOptions { RenderingMode = MacRenderingModes() })
            // Linux (X11, also under XWayland): the window class matches StartupWMClass in the .desktop file, so docks
            // and task bars show the app's icon and name for its window.
            .With(new X11PlatformOptions { WmClass = AppId })
            .LogToTrace();

    private static IReadOnlyList<AvaloniaNativeRenderingMode> MacRenderingModes() =>
        string.Equals(Environment.GetEnvironmentVariable("SYNCHROMESH_RENDERER"), "metal", StringComparison.OrdinalIgnoreCase)
            ? [AvaloniaNativeRenderingMode.Metal, AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Software]
            : [AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Software];
}
