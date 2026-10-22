using Xunit;

namespace Synchro.Tests;

/// <summary>The app identifier is written in four places; macOS and Windows key permissions on it, so they must agree.</summary>
public class AppIdentifierTests
{
    private const string Expected = "nl.vinch.synchromesh";

    [Fact]
    public void Every_place_uses_the_same_app_identifier()
    {
        var root = RepositoryRoot();

        Assert.Equal(Expected, Synchro.Desktop.Program.AppId);
        Assert.Contains($"<string>{Expected}</string>", File.ReadAllText(Path.Combine(root, "build/macos/Info.plist")));
        Assert.Contains($"!define APP_ID \"{Expected}\"", File.ReadAllText(Path.Combine(root, "build/windows/installer.nsi")));
        Assert.Contains($"AppId = \"{Expected}\"", File.ReadAllText(Path.Combine(root, "src/Synchro.Platform.Windows/WindowsNotifier.cs")));
        Assert.Contains($"DesktopEntry = \"{Expected}\"", File.ReadAllText(Path.Combine(root, "src/Synchro.Platform.Linux/DesktopNotifier.cs")));
        Assert.Contains($"APP_ID=\"{Expected}\"", File.ReadAllText(Path.Combine(root, "build/linux/install.sh")));
        Assert.True(File.Exists(Path.Combine(root, $"build/linux/{Expected}.desktop")));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synchromesh.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
