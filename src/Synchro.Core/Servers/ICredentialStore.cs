namespace Synchro.Core.Servers;

/// <summary>
/// Secure storage for server passwords. macOS: Keychain. Windows: Credential Manager. Linux: Secret Service.
/// </summary>
public interface ICredentialStore
{
    /// <summary>What users call this place, for sentences such as "Instead of the Keychain".</summary>
    string Name { get; }

    Task<string?> GetPasswordAsync(Guid profileId, CancellationToken ct = default);
    Task SetPasswordAsync(Guid profileId, string password, CancellationToken ct = default);

    /// <summary>Removes the stored password. Does nothing when none is stored.</summary>
    Task DeleteAsync(Guid profileId, CancellationToken ct = default);
}
