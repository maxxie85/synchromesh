using CommunityToolkit.Mvvm.ComponentModel;

namespace Synchro.ViewModels.Settings;

/// <summary>Sections of the Settings window, in tab order.</summary>
public enum SettingsSection
{
    Servers,
    Transmission,
    Appearance,
}

/// <summary>The Settings window: servers, the connected server's preferences, and appearance. Changes apply right away; there is nothing to save.</summary>
public sealed partial class SettingsViewModel(
    ServersViewModel servers,
    ServerPreferencesViewModel preferences,
    AppearanceViewModel appearance,
    PasswordsViewModel passwords) : ObservableObject
{
    public ServersViewModel Servers { get; } = servers;
    public ServerPreferencesViewModel Preferences { get; } = preferences;
    public AppearanceViewModel Appearance { get; } = appearance;
    public PasswordsViewModel Passwords { get; } = passwords;

    [ObservableProperty] private int _selectedSectionIndex;

    public async Task LoadAsync(SettingsSection section, CancellationToken ct = default)
    {
        SelectedSectionIndex = (int)section;
        await Servers.LoadAsync(ct);
        await Preferences.LoadAsync(ct);
        await Appearance.LoadAsync(ct);
        await Passwords.LoadAsync(ct);
    }
}
