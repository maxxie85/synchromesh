namespace Synchro.Core.Servers;

/// <summary>Where server passwords are kept.</summary>
public enum PasswordStorage
{
    /// <summary>The system's: Keychain, Credential Manager or the Secret Service keyring (the default).</summary>
    Keyring,

    /// <summary>The app's own encrypted file (<see cref="IPasswordFile"/>); the keyring isn't touched at all.</summary>
    File,
}

/// <summary>Chooses where passwords are kept, and moves the saved ones when that changes.</summary>
public interface IPasswordStorage
{
    /// <summary>The system keyring's name ("the Keychain").</summary>
    string KeyringName { get; }

    Task<PasswordStorage> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Moves the passwords of <paramref name="profileIds"/> to <paramref name="target"/>, then remembers the choice.
    /// Copies first and removes from the old place only when every copy worked, so a failure loses nothing (and
    /// leaves the choice as it was).
    /// </summary>
    Task SwitchAsync(PasswordStorage target, IReadOnlyCollection<Guid> profileIds, CancellationToken ct = default);
}
