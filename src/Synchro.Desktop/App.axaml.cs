using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SimpleInjector;
using Synchro.AppServices.Notifications;
using Synchro.Core.Notifications;
using Synchro.Core.Settings;
using Synchro.ViewModels.Services;
using Synchro.Desktop.Services;
using Synchro.Desktop.Views;
using Synchro.Platform.MacOS;
using Synchro.ViewModels;

namespace Synchro.Desktop;

public partial class App : Application
{
    private Container? _container;

    /// <summary>Set by <see cref="Program"/> on platforms where later launches hand their arguments to this copy.</summary>
    internal static SingleInstance? SingleInstance { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _container = CompositionRoot.Create(AppDataPath.Resolve());
            _container.Verify(); // Fail fast on any wiring mistake, not on first click.

            // The saved theme before the first window opens, so it never flashes the system theme first. Settings are a
            // small local file; the store doesn't capture the UI context, so waiting here can't deadlock.
            var settings = _container.GetInstance<IAppSettingsStore>().LoadAsync().GetAwaiter().GetResult();
            _container.GetInstance<IThemeService>().Apply(settings.Theme);
            // Help > Log UI stalls, if the user left it on: from the very start, so first-time slowness shows too.
            if (settings.LogUiStalls)
                _container.GetInstance<IStallLogging>().SetEnabled(true);

            // Notifications for finished downloads and errors: ask macOS for permission once, then start listening.
            _container.GetInstance<INotifier>().Initialize();
            _container.GetInstance<TorrentNotifications>();

            // Before the main loop runs, or the file or magnet link the app was launched with would be missed.
            _container.GetInstance<ActivationListener>().Attach(this, desktop.Args ?? []);

            var mainViewModel = _container.GetInstance<MainWindowViewModel>();
            DataContext = mainViewModel; // for the app menu in App.axaml
            desktop.MainWindow = new MainWindow { DataContext = mainViewModel };
            desktop.Exit += (_, _) =>
            {
                SingleInstance?.Dispose();
                _container.Dispose();
            };

            // A later launch: open what it was given, and bring this window to the front.
            var listener = _container.GetInstance<ActivationListener>();
            SingleInstance?.Listen(args => Dispatcher.UIThread.Post(() =>
            {
                listener.Forward(args);
                if (desktop.MainWindow is { } window) BringToFront(window);
            }));
        }

        base.OnFrameworkInitializationCompleted();
    }

    // The macOS app menu (App.axaml), which only exists on macOS.
    private void OnHide(object? sender, EventArgs e) => MacApplicationMenu.Hide();
    private void OnHideOthers(object? sender, EventArgs e) => MacApplicationMenu.HideOthers();
    private void OnShowAll(object? sender, EventArgs e) => MacApplicationMenu.ShowAll();

    private void OnQuit(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.TryShutdown();
    }

    // Windows only lets the foreground app take focus; briefly topmost brings the window up anyway.
    private static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
    }
}
