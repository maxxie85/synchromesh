namespace Synchro.Core.Servers;

/// <summary>
/// The app's own encrypted password file: for systems without a keyring (a Steam Deck), or for users who'd rather
/// not give the app access to theirs. The passwords are encrypted with a random key kept in a separate file that
/// only the user can read. Without a master password that protects against the password file being copied, backed
/// up or shared on its own, but not against programs running as the user; with one, the key itself is encrypted
/// and the passwords are safe without it.
/// </summary>
public interface IPasswordFile
{
    /// <summary>True once the user agreed on this computer (the key exists); passwords then go here when there's no keyring.</summary>
    bool IsEnabled { get; }

    /// <summary>The key is protected by a master password, asked for once per run.</summary>
    bool HasMasterPassword { get; }

    /// <summary>Creates the key. Does nothing when it already exists.</summary>
    void Enable();

    /// <summary>
    /// Protects the key with <paramref name="masterPassword"/>, replaces the master password, or (null) removes it.
    /// Asks for the current master password first when the file is still locked. The passwords themselves don't change.
    /// </summary>
    /// <exception cref="PasswordFileLockedException">The current master password wasn't entered.</exception>
    Task SetMasterPasswordAsync(string? masterPassword, CancellationToken ct = default);
}

/// <summary>Asks the user for the password file's master password.</summary>
public interface IMasterPasswordPrompt
{
    /// <param name="problem">Why it's asked again ("That isn't the master password."), or null the first time.</param>
    /// <param name="folder">Where the files are, for the "forgot it" advice.</param>
    /// <returns>The master password, or null when the user cancels.</returns>
    Task<string?> AskAsync(string? problem, string folder, CancellationToken ct = default);
}

/// <summary>The password file has a master password and the user didn't enter it.</summary>
public sealed class PasswordFileLockedException()
    : Exception("The saved passwords are locked: the master password wasn't entered.");
