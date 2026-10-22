using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Synchro.Desktop.Views;

/// <summary>A plain message box. Deliberately code-behind only: it has no state worth a view model.</summary>
public partial class MessageWindow : Window
{
    public MessageWindow() : this("", "")
    {
    }

    public MessageWindow(string title, string message)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
