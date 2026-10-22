namespace Synchro.Desktop;

internal static class AppDataPath
{
    private const string AppFolder = "Synchromesh";

    /// <summary>Per-user data folder. .NET maps ApplicationData to ~/.config on macOS, so we use the native location there.</summary>
    public static string Resolve() =>
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", AppFolder)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolder);
}
