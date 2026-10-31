namespace Synchro.Core.Settings;

public enum ThemePreference
{
    /// <summary>Follow the OS (macOS or Windows light/dark mode), including when it changes.</summary>
    System,
    Light,
    Dark,
}

/// <summary>App-wide preferences that aren't about one server.</summary>
/// <param name="LastServerId">The server connected to last; the app reconnects to it on start.</param>
/// <param name="LogUiStalls">Help > Log UI stalls, kept on across restarts so it also catches the first moments.</param>
/// <param name="PasswordStorage">Where server passwords are kept (Settings > Servers).</param>
/// <param name="ListSort">The torrent list's sort column and direction; null is queue order.</param>
public sealed record AppSettings(
    ThemePreference Theme = ThemePreference.System,
    Guid? LastServerId = null,
    bool LogUiStalls = false,
    Servers.PasswordStorage PasswordStorage = Servers.PasswordStorage.Keyring,
    TorrentSort? ListSort = null);

public interface IAppSettingsStore
{
    /// <summary>The saved settings, or the defaults when there are none (or the file is unreadable).</summary>
    Task<AppSettings> LoadAsync(CancellationToken ct = default);

    /// <summary>Reads, changes and writes the settings as one step, so two changes can't overwrite each other.</summary>
    Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken ct = default);
}
