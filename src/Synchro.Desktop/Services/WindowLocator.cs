using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Synchro.Desktop.Services;

internal static class WindowLocator
{
    public static Window MainWindow()
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
           ?? throw new InvalidOperationException("The main window must be open before showing a dialog.");

    /// <summary>The window the user is looking at, so pickers and dialogs attach to it as sheets.</summary>
    public static Window Active()
    {
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        return lifetime?.Windows.FirstOrDefault(w => w.IsActive) ?? MainWindow();
    }
}
