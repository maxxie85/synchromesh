namespace Synchro.Core.Servers;

/// <summary>
/// The app speaks JSON-RPC 2.0 with snake_case names, introduced in Transmission 4.1.0
/// (rpc_version_semver 6.0.0). Older servers are intentionally not supported.
/// </summary>
public static class RpcVersion
{
    public static readonly Version Minimum = new(6, 0, 0);

    /// <summary>
    /// The major version this app was built and tested against. A higher major version may have breaking changes
    /// (that's what semver promises), so the app connects but says so.
    /// </summary>
    public const int TestedMajor = 6;

    /// <summary>Always three parts ("6.1" reads as 6.1.0), so comparisons with <c>new Version(6, 1, 0)</c> hold.</summary>
    public static bool TryParse(string? semver, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(semver)) return false;

        // Drop any pre-release or build suffix ("6.1.0-beta.1+abc").
        var core = semver.Split('-', '+')[0];
        if (!Version.TryParse(core, out var parsed)) return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        return true;
    }

    public static bool IsSupported(string? semver) => TryParse(semver, out var v) && v >= Minimum;
}
