namespace Synchro.Core.Servers;

/// <summary>
/// What the connected server's RPC version (rpc_version_semver) allows. Everything the app uses today exists in
/// 6.0.0. When the app starts using something a later version added, give it a property here
/// (<c>public bool Foo => AtLeast(6, 2);</c>) and hide or disable the feature when it's false, instead of letting
/// the server answer with an error halfway through an action.
/// </summary>
public sealed record ServerCapabilities(Version Rpc)
{
    /// <summary>The oldest server the app accepts; also what code that doesn't know better can assume.</summary>
    public static ServerCapabilities Baseline { get; } = new(RpcVersion.Minimum);

    /// <summary>"6.0.0".</summary>
    public string RpcText => $"{Rpc.Major}.{Rpc.Minor}.{Math.Max(Rpc.Build, 0)}";

    /// <summary>A newer major version than the app was tested with: it may have changed things the app relies on.</summary>
    public bool IsUntested => Rpc.Major > RpcVersion.TestedMajor;

    /// <summary>A plain sentence for the status bar and the server setup, or null when there's nothing to say.</summary>
    public string? Warning => IsUntested
        ? $"Its RPC version {RpcText} is newer than this app was tested with, so some things may not work."
        : null;

    public bool AtLeast(int major, int minor) => Rpc >= new Version(major, minor, 0);
}
