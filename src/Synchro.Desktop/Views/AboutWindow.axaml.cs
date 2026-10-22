using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Synchro.Desktop.Views;

public partial class AboutWindow : Window
{
    public AboutWindow() => InitializeComponent();

    private void OnLicensesClick(object? sender, RoutedEventArgs e) => new LicensesWindow().ShowDialog(this);

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
