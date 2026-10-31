using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Synchro.Desktop.Views;

public partial class ToolbarView : UserControl
{
    public ToolbarView() => InitializeComponent();

    /// <summary>View > Search (Cmd+F / Ctrl+F): focus is a view concern, so the window asks the toolbar.</summary>
    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    // The server menu is a Flyout (it holds a list), which doesn't close by itself on a click like a menu does.
    // Close it after the click: a button runs its command after the Click event, and closing the flyout first
    // detaches the button, which clears its command binding, so nothing would happen.
    private void OnServerMenuItemClick(object? sender, RoutedEventArgs e)
        => Dispatcher.UIThread.Post(() => ServerMenuButton.Flyout?.Hide());
}
