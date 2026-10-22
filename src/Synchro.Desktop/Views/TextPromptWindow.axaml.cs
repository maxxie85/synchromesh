using Avalonia.Controls;

namespace Synchro.Desktop.Views;

public partial class TextPromptWindow : Window
{
    public TextPromptWindow() => InitializeComponent();

    // Focus the field and select a name, so typing replaces it (as Finder's rename does).
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Input.Focus();
        Input.SelectAll();
    }
}
