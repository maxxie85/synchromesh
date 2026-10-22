using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;

namespace Synchro.Desktop.Views;

/// <summary>The app's own license, then the third-party notices (LICENSE and THIRD-PARTY-NOTICES.txt, embedded).</summary>
public partial class LicensesWindow : Window
{
    public LicensesWindow()
    {
        InitializeComponent();
        NoticesText.Text = $"Synchromesh\n\n{Read("LICENSE.txt")}\n\n\n{Read("THIRD-PARTY-NOTICES.txt")}";
    }

    private static string Read(string asset)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://Synchromesh/Assets/{asset}"));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
