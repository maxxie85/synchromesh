using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Synchro.Core.Servers;
using static Synchro.Platform.Windows.WindowsInterop;

namespace Synchro.Platform.Windows;

/// <summary>
/// Server passwords in Windows Credential Manager ("Generic credentials", visible in Control Panel), one entry per
/// server profile: Synchromesh/&lt;profile id&gt;. The Windows counterpart of the macOS Keychain store.
/// </summary>
public sealed class CredentialManagerStore : ICredentialStore
{
    public string Name => "Windows Credential Manager";

    private const string AppName = "Synchromesh";

    public Task<string?> GetPasswordAsync(Guid profileId, CancellationToken ct = default)
    {
        if (!CredRead(Target(profileId), CredTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound
                ? Task.FromResult<string?>(null)
                : Task.FromException<string?>(new Win32Exception(error, "Couldn't read the password from Credential Manager."));
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlobSize == 0) return Task.FromResult<string?>("");

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Task.FromResult<string?>(Encoding.Unicode.GetString(bytes));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public Task SetPasswordAsync(Guid profileId, string password, CancellationToken ct = default)
    {
        var bytes = Encoding.Unicode.GetBytes(password);
        var blob = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1));
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = Target(profileId),
                Comment = $"{AppName} server password",
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = AppName,
            };

            return CredWrite(ref credential, 0)
                ? Task.CompletedTask
                : Task.FromException(new Win32Exception(Marshal.GetLastWin32Error(), "Couldn't save the password in Credential Manager."));
        }
        finally
        {
            // Don't leave the password in unmanaged memory longer than needed.
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Marshal.FreeHGlobal(blob);
        }
    }

    public Task DeleteAsync(Guid profileId, CancellationToken ct = default)
    {
        if (CredDelete(Target(profileId), CredTypeGeneric, 0)) return Task.CompletedTask;

        var error = Marshal.GetLastWin32Error();
        return error == ErrorNotFound
            ? Task.CompletedTask
            : Task.FromException(new Win32Exception(error, "Couldn't remove the password from Credential Manager."));
    }

    private static string Target(Guid profileId) => $"{AppName}/{profileId:D}";
}
