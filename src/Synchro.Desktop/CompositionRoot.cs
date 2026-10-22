using SimpleInjector;
using Synchro.AppServices.Torrents;
using Synchro.Core.Torrents;
using Synchro.AppServices;
using Synchro.AppServices.Connection;
using Synchro.AppServices.Credentials;
using Synchro.AppServices.Notifications;
using Synchro.Core.Notifications;
using Synchro.Core.Servers;
using Synchro.Core.Settings;
using Synchro.Desktop.Services;
using Synchro.Platform.Linux;
using Synchro.Platform.MacOS;
using Synchro.Transmission;
using Synchro.ViewModels;
using Synchro.ViewModels.Details;
using Synchro.ViewModels.Settings;
using Synchro.ViewModels.Services;

namespace Synchro.Desktop;

/// <summary>The one place that knows about concrete types. Everything else depends on interfaces.</summary>
public static class CompositionRoot
{
    public static Container Create(string appDataDirectory)
    {
        var container = new Container();

        // Infrastructure
        container.RegisterInstance(TimeProvider.System);
        container.RegisterSingleton<ITransmissionConnectionFactory, TransmissionConnectionFactory>();
        container.RegisterSingleton<IConnectionTester, ConnectionTester>();
        container.RegisterSingleton<ITransmissionClientFactory, TransmissionClientFactory>();
        container.RegisterSingleton<IServerProfileStore>(
            () => new JsonServerProfileStore(Path.Combine(appDataDirectory, "profiles.json")));
        container.RegisterSingleton<IAppSettingsStore>(
            () => new JsonAppSettingsStore(Path.Combine(appDataDirectory, "settings.json")));

        Type keyringType;

        // Platform seam. The Windows types only exist in the Windows build (see Synchro.Desktop.csproj), hence the #if:
        // this and Program are the only places that know about platforms.
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            keyringType = typeof(Synchro.Platform.Windows.CredentialManagerStore);
            container.RegisterSingleton<INotifier, Synchro.Platform.Windows.WindowsNotifier>();
        }
        else
#endif
        if (OperatingSystem.IsMacOS())
        {
            keyringType = typeof(KeychainCredentialStore);
            container.RegisterSingleton<INotifier, MacNotifier>();
        }
        else if (OperatingSystem.IsLinux())
        {
            keyringType = typeof(SecretServiceCredentialStore);
            container.RegisterSingleton<INotifier, DesktopNotifier>();
        }
        else
            throw new PlatformNotSupportedException(
                "This build has no credential store for this platform. Publish for macOS (osx-*), Windows (win-*) or Linux (linux-*).");

        // Passwords: the system keyring above, or the app's own encrypted file when chosen in Settings or when there's
        // no keyring (a Steam Deck). AppCredentialStore decides; it is both the store and the switch between the two.
        container.Register(keyringType, keyringType, Lifestyle.Singleton);
        container.RegisterSingleton<IMasterPasswordPrompt, MasterPasswordPrompt>();
        var passwordFile = Lifestyle.Singleton.CreateRegistration(
            () => new PasswordFile(appDataDirectory, container.GetInstance<IMasterPasswordPrompt>()), container);
        container.AddRegistration<PasswordFile>(passwordFile);
        container.AddRegistration<IPasswordFile>(passwordFile);
        var credentials = Lifestyle.Singleton.CreateRegistration(() => new AppCredentialStore(
            (ICredentialStore)container.GetInstance(keyringType), container.GetInstance<PasswordFile>(),
            container.GetInstance<IAppSettingsStore>()), container);
        container.AddRegistration<ICredentialStore>(credentials);
        container.AddRegistration<IPasswordStorage>(credentials);

        // Application services. One poller instance serves three roles: control (connection service),
        // read-only feed and the selected torrent's details (view models).
        var poller = Lifestyle.Singleton.CreateRegistration<TorrentPoller>(container);
        container.AddRegistration<ITorrentPoller>(poller);
        container.AddRegistration<ITorrentFeed>(poller);
        container.AddRegistration<ITorrentDetailsFeed>(poller);
        container.RegisterSingleton<ITorrentActions, TorrentActions>();
        container.RegisterSingleton<ISpeedLimitService, SpeedLimitService>();
        container.RegisterSingleton<IAddTorrentService, AddTorrentService>();
        container.RegisterSingleton<IStorageService, StorageService>();
        container.RegisterSingleton<IConnectionService, ConnectionService>();
        container.RegisterSingleton<IServerPreferencesService, ServerPreferencesService>();
        container.RegisterSingleton<TorrentNotifications>();

        // UI services
        container.RegisterInstance<Func<ServerSetupViewModel>>(() => container.GetInstance<ServerSetupViewModel>());
        container.RegisterInstance<Func<AddTorrentViewModel>>(() => container.GetInstance<AddTorrentViewModel>());
        container.RegisterInstance<Func<SettingsViewModel>>(() => container.GetInstance<SettingsViewModel>());
        container.RegisterSingleton<IThemeService, ThemeService>();
        container.RegisterSingleton<IFilePicker, FilePicker>();
        container.RegisterSingleton<IClipboardService, ClipboardService>();
        container.RegisterSingleton<IUrlLauncher, UrlLauncher>();
        container.RegisterSingleton<IStallLogging>(() => new UiStallMonitor(Path.Combine(appDataDirectory, "logs", "ui-stalls.log")));
        container.RegisterSingleton<IAddTorrentFlow, AddTorrentFlow>();
        container.RegisterSingleton<IDialogService, DialogService>();
        container.RegisterSingleton<IExternalAddQueue, ExternalAddQueue>();
        container.RegisterSingleton<ActivationListener>();

        // View models: one main window, so those are singletons; dialogs get a fresh instance each time.
        container.RegisterSingleton<StatusBarViewModel>();
        container.RegisterSingleton<TorrentListViewModel>();
        container.RegisterSingleton<ToolbarViewModel>();
        container.RegisterSingleton<HelpViewModel>();
        container.RegisterSingleton<StatisticsViewModel>();
        container.RegisterSingleton<SpeedHistoryViewModel>();
        container.RegisterSingleton<StorageViewModel>();
        container.RegisterSingleton<AltSpeedViewModel>();
        container.RegisterSingleton<ServersViewModel>();
        container.RegisterSingleton<AppearanceViewModel>();
        container.RegisterSingleton<PasswordsViewModel>();
        container.RegisterSingleton<TorrentDetailsViewModel>();
        container.RegisterSingleton<MainWindowViewModel>();
        container.Register<ServerSetupViewModel>();
        container.Register<AddTorrentViewModel>();
        container.Register<SettingsViewModel>();
        container.Register<ServerPreferencesViewModel>();

        return container;
    }
}
