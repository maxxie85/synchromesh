using System.Reflection;

namespace Synchro.Desktop;

internal static class AppInfo
{
    /// <summary>The app version without the SDK's "+&lt;commit&gt;" suffix: what users need to see.</summary>
    public static string Version { get; } =
        (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown")
        .Split('+')[0];
}
