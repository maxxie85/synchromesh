using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Synchro.Desktop.Views;

/// <summary>A yes/no question with a named confirm button. Code-behind only, like <see cref="MessageWindow"/>.</summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow() : this("", "", "OK")
    {
    }

    public ConfirmWindow(string title, string message, string confirmText)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
