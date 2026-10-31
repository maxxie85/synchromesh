using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NSubstitute;
using SimpleInjector;
using Synchro.Core.Notifications;
using Synchro.Core.Servers;
using Synchro.Core.Settings;
using Synchro.Core.Torrents;
using Synchro.Desktop;
using Synchro.Desktop.Views;
using Synchro.Screenshots;
using Synchro.ViewModels;
using Synchro.ViewModels.Details;
using Synchro.ViewModels.Settings;

// Renders the README screenshots: the real windows, styles and view models, headless, against DemoServer.
//
//   dotnet run --project tools/Synchro.Screenshots [-- <output folder>]     (default docs/screenshots)
//
// Each picture comes in a light and a dark variant (name-light.png, name-dark.png).

// English number and date formats whatever this computer uses: the README is in English.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentCulture =
    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

var output = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine(RepositoryRoot(), "docs", "screenshots"));
Directory.CreateDirectory(output);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

foreach (var (variant, suffix) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
{
    Application.Current!.RequestedThemeVariant = variant;
    var data = Directory.CreateTempSubdirectory("synchromesh-screenshots-");
    using var container = DemoContainer(data.FullName);

    // Main window: connects to the demo server, polls, shows the featured torrent's details.
    var main = container.GetInstance<MainWindowViewModel>();
    var window = new MainWindow { DataContext = main, Width = 1280, Height = 800 };
    window.Show();
    Pump(TimeSpan.FromSeconds(10), () => main.StatusBar.IsConnected);
    container.GetInstance<SpeedHistoryViewModel>().Seed(DemoServer.SpeedHistory());
    Pump(TimeSpan.FromSeconds(5));

    var list = window.GetVisualDescendants().OfType<DataGrid>().First(l => l.Name == "TorrentGrid");
    list.SelectedItem = main.TorrentList.VisibleTorrents.First(t => t.Name == DemoServer.Featured);
    Pump(TimeSpan.FromSeconds(3));
    Save(window, "main", suffix);

    main.Details.SelectedTabIndex = TorrentDetailsViewModel.FilesTabIndex;
    Pump(TimeSpan.FromSeconds(3));
    Save(window, "files", suffix);

    main.Details.SelectedTabIndex = 2; // Peers
    Pump(TimeSpan.FromSeconds(3));
    Save(window, "peers", suffix);

    // Settings > Transmission: the server's own preferences.
    var settings = container.GetInstance<SettingsViewModel>();
    Await(settings.LoadAsync(SettingsSection.Transmission));
    var settingsWindow = new SettingsWindow { DataContext = settings, Width = 680, Height = 820 };
    settingsWindow.Show();
    Pump(TimeSpan.FromSeconds(2));
    Save(settingsWindow, "settings", suffix);
    settingsWindow.Close();

    // Adding a .torrent: the file list to choose from.
    var add = container.GetInstance<AddTorrentViewModel>();
    var addWindow = new AddTorrentWindow { DataContext = add };
    addWindow.Show();
    Await(add.InitializeAsync(new AddTorrentSource.TorrentFile(DemoServer.AddFileName, DemoServer.TorrentFile())));
    Pump(TimeSpan.FromSeconds(2));
    Save(addWindow, "add", suffix);
    addWindow.Close();

    window.Close();
    Pump(TimeSpan.FromSeconds(1));
    data.Delete(recursive: true);
}

Console.WriteLine($"Screenshots in {output}");
return;

// The app's own composition, with the server, the password store and notifications replaced.
static SimpleInjector.Container DemoContainer(string dataFolder)
{
    var container = CompositionRoot.Create(dataFolder);
    container.Options.AllowOverridingRegistrations = true;

    var tester = Substitute.For<IConnectionTester>();
    tester.TestAsync(default!, default, default).ReturnsForAnyArgs(new ConnectionTestResult.Success("4.1.3", ServerCapabilities.Baseline));
    container.RegisterInstance(tester);

    var client = DemoServer.Client();
    var clients = Substitute.For<ITransmissionClientFactory>();
    clients.Create(default!, default, default!).ReturnsForAnyArgs(client);
    container.RegisterInstance(clients);

    var credentials = Substitute.For<ICredentialStore>();
    credentials.GetPasswordAsync(default, default).ReturnsForAnyArgs((string?)null);
    container.RegisterInstance(credentials);
    container.RegisterInstance(Substitute.For<INotifier>());

    var home = new ServerProfile(Guid.NewGuid(), "Home NAS", "nas.local");
    var profiles = container.GetInstance<IServerProfileStore>();
    Await(profiles.SaveAsync(home));
    Await(profiles.SaveAsync(new ServerProfile(Guid.NewGuid(), "Seedbox", "seedbox.example.org", 443, UseHttps: true)));
    Await(container.GetInstance<IAppSettingsStore>().UpdateAsync(s => s with { LastServerId = home.Id }));
    return container;
}

void Save(Window target, string name, string suffix)
{
    var path = Path.Combine(output, $"{name}-{suffix}.png");
    target.CaptureRenderedFrame()!.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    Console.WriteLine(path);
}

// Runs the UI thread (bindings, polls, the speed graph's animation) for a while, or until done.
static void Pump(TimeSpan duration, Func<bool>? until = null)
{
    var end = DateTime.UtcNow + duration;
    while (DateTime.UtcNow < end && until?.Invoke() != true)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(15);
    }
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
}

static void Await(Task task)
{
    Pump(TimeSpan.FromSeconds(10), () => task.IsCompleted);
    task.GetAwaiter().GetResult();
}

static string RepositoryRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synchromesh.slnx")))
        dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
