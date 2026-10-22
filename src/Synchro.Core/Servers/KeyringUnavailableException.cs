namespace Synchro.Core.Servers;

/// <summary>
/// The system has no keyring to keep passwords in (on Linux: nothing offers the Secret Service, or libsecret is
/// missing). Unlike other credential errors, the password file (<see cref="IPasswordFile"/>) can take over.
/// </summary>
public sealed class KeyringUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
