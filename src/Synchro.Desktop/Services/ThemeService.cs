using Avalonia;
using Avalonia.Styling;
using Synchro.Core.Settings;
using Synchro.ViewModels.Services;

namespace Synchro.Desktop.Services;

/// <summary>
/// Sets the app's theme variant. The Material theme uses BaseTheme="Inherit", so it follows: "System" is Avalonia's
/// Default variant, which tracks the OS light/dark setting live.
/// </summary>
public sealed class ThemeService : IThemeService
{
    public void Apply(ThemePreference theme)
    {
        if (Application.Current is not { } app) return;

        app.RequestedThemeVariant = theme switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
