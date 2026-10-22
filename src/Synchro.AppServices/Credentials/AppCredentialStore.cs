using Synchro.Core.Servers;
using Synchro.Core.Settings;

namespace Synchro.AppServices.Credentials;

/// <summary>
/// The credential store the app uses: the system keyring by default, or the app's encrypted <see cref="PasswordFile"/>
/// when the user chose it (<see cref="PasswordStorage.File"/>; the keyring is then never touched) or when there's no
/// keyring and the user agreed to the file. A password in the file wins on reading, so a keyring that appears later
/// doesn't hide it; saving to a working keyring moves the password out of the file.
/// </summary>
public sealed class AppCredentialStore(ICredentialStore keyring, PasswordFile file, IAppSettingsStore settings)
    : ICredentialStore, IPasswordStorage
{
    public string Name => keyring.Name;

    public string KeyringName => keyring.Name;

    public async Task<PasswordStorage> GetAsync(CancellationToken ct = default)
        => (await settings.LoadAsync(ct).ConfigureAwait(false)).PasswordStorage;

    public async Task<string?> GetPasswordAsync(Guid profileId, CancellationToken ct = default)
    {
        if (await file.ContainsAsync(profileId, ct).ConfigureAwait(false))
            return await file.GetPasswordAsync(profileId, ct).ConfigureAwait(false);
        if (await GetAsync(ct).ConfigureAwait(false) == PasswordStorage.File)
            return null;

        try
        {
            return await keyring.GetPasswordAsync(profileId, ct).ConfigureAwait(false);
        }
        catch (KeyringUnavailableException)
        {
            return null; // nothing could have been saved there
        }
    }

    /// <exception cref="KeyringUnavailableException">No keyring, and the password file isn't enabled yet.</exception>
    public async Task SetPasswordAsync(Guid profileId, string password, CancellationToken ct = default)
    {
        if (await GetAsync(ct).ConfigureAwait(false) == PasswordStorage.File)
        {
            file.Enable();
            await file.SetPasswordAsync(profileId, password, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await keyring.SetPasswordAsync(profileId, password, ct).ConfigureAwait(false);
        }
        catch (KeyringUnavailableException) when (file.IsEnabled)
        {
            await file.SetPasswordAsync(profileId, password, ct).ConfigureAwait(false);
            return;
        }

        await file.DeleteAsync(profileId, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid profileId, CancellationToken ct = default)
    {
        await file.DeleteAsync(profileId, ct).ConfigureAwait(false);
        if (await GetAsync(ct).ConfigureAwait(false) == PasswordStorage.File) return;

        try
        {
            await keyring.DeleteAsync(profileId, ct).ConfigureAwait(false);
        }
        catch (KeyringUnavailableException)
        {
            // Nothing can be stored there either.
        }
    }

    public async Task SwitchAsync(PasswordStorage target, IReadOnlyCollection<Guid> profileIds, CancellationToken ct = default)
    {
        if (await GetAsync(ct).ConfigureAwait(false) == target) return;

        if (target == PasswordStorage.File)
        {
            file.Enable();
            var moved = new List<Guid>();
            foreach (var id in profileIds)
            {
                if (await file.ContainsAsync(id, ct).ConfigureAwait(false)) continue; // already there (the no-keyring case)
                string? password;
                try { password = await keyring.GetPasswordAsync(id, ct).ConfigureAwait(false); }
                catch (KeyringUnavailableException) { continue; }
                if (string.IsNullOrEmpty(password)) continue;

                await file.SetPasswordAsync(id, password, ct).ConfigureAwait(false);
                moved.Add(id);
            }

            await RememberAsync(target, ct).ConfigureAwait(false);
            // The file has them all now; a keyring entry that won't go away is only a leftover, never read again.
            foreach (var id in moved)
            {
                try { await keyring.DeleteAsync(id, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OperationCanceledException) { }
            }
        }
        else
        {
            var moved = new List<Guid>();
            foreach (var id in profileIds)
            {
                if (await file.GetPasswordAsync(id, ct).ConfigureAwait(false) is not { } password) continue;
                await keyring.SetPasswordAsync(id, password, ct).ConfigureAwait(false); // throws: nothing removed yet
                moved.Add(id);
            }

            await RememberAsync(target, ct).ConfigureAwait(false);
            foreach (var id in moved)
                await file.DeleteAsync(id, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private Task RememberAsync(PasswordStorage target, CancellationToken ct)
        => settings.UpdateAsync(s => s with { PasswordStorage = target }, ct);
}
