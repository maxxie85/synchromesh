using CommunityToolkit.Mvvm.ComponentModel;
using Synchro.Core.Settings;
using Synchro.ViewModels.Services;

namespace Synchro.ViewModels.Settings;

public sealed record ThemeOption(ThemePreference Value, string Label, string Description);

/// <summary>The Appearance section: light, dark, or follow the OS. Applied at once and remembered.</summary>
public sealed partial class AppearanceViewModel(IAppSettingsStore settings, IThemeService theme) : ObservableObject
{
    public IReadOnlyList<ThemeOption> Themes { get; } =
    [
        new(ThemePreference.System, "System", "Light or dark, following your computer's setting."),
        new(ThemePreference.Light, "Light", "Always light."),
        new(ThemePreference.Dark, "Dark", "Always dark."),
    ];

    private bool _loading;

    [ObservableProperty] private ThemeOption? _selectedTheme;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var current = await settings.LoadAsync(ct);
        _loading = true;
        SelectedTheme = Themes.First(t => t.Value == current.Theme);
        _loading = false;
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (_loading || value is null) return;

        theme.Apply(value.Value);
        _ = SaveAsync(value.Value);
    }

    private async Task SaveAsync(ThemePreference value)
    {
        try
        {
            await settings.UpdateAsync(s => s with { Theme = value });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The theme still applies for now; it just won't be remembered for the next start.
        }
    }
}
