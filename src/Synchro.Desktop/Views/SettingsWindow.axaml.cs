using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Synchro.ViewModels.Settings;

namespace Synchro.Desktop.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    private void OnDoneClick(object? sender, RoutedEventArgs e) => Close();

    // Double-clicking a server connects to it, like the Connect button.
    private void OnServerDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is SettingsViewModel { Servers: var servers } && servers.ConnectCommand.CanExecute(null))
            servers.ConnectCommand.Execute(null);
    }
}
