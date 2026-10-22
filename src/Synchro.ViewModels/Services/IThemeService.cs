using Synchro.Core.Settings;

namespace Synchro.ViewModels.Services;

/// <summary>Switches the app between light, dark and following the OS. A view concern, so the view layer implements it.</summary>
public interface IThemeService
{
    void Apply(ThemePreference theme);
}
