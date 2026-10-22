using System.ComponentModel;
using Avalonia.Controls;
using Synchro.ViewModels.Details;

namespace Synchro.Desktop.Views;

public partial class FileTreeView : UserControl
{
    public FileTreeView() => InitializeComponent();

    // The row menu only offers renaming, which files chosen in the Add dialog can't do: no menu there.
    private void OnRowMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is ContextMenu { DataContext: FileNodeViewModel { CanRename: false } })
            e.Cancel = true;
    }
}
