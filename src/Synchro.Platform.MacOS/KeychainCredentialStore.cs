using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Synchro.Core.Servers;
using static Synchro.Platform.MacOS.SecurityInterop;

namespace Synchro.Platform.MacOS;

/// <summary>Stores server passwords in the macOS login Keychain, one item per server profile.</summary>
[SupportedOSPlatform("macos")]
public sealed class KeychainCredentialStore : ICredentialStore
{
    public string Name => "the Keychain";

    private const string ServiceName = "Synchromesh";

    public Task<string?> GetPasswordAsync(Guid profileId, CancellationToken ct = default)
        => Task.FromResult(Get(profileId.ToString("D")));

    public Task SetPasswordAsync(Guid profileId, string password, CancellationToken ct = default)
    {
        Set(profileId.ToString("D"), password);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid profileId, CancellationToken ct = default)
    {
        Delete(profileId.ToString("D"));
        return Task.CompletedTask;
    }

    private static string? Get(string account)
    {
        var status = Find(account, out var length, out var data, out var item);
        if (status == ErrSecItemNotFound) return null;
        ThrowIfError(status);

        try
        {
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, (int)length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            Release(data, item);
        }
    }

    private static void Set(string account, string password)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var status = Find(account, out _, out var data, out var item);

        if (status == ErrSecSuccess)
        {
            try { ThrowIfError(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)passwordBytes.Length, passwordBytes)); }
            finally { Release(data, item); }
            return;
        }

        if (status != ErrSecItemNotFound) ThrowIfError(status);

        var service = Encoding.UTF8.GetBytes(ServiceName);
        var accountBytes = Encoding.UTF8.GetBytes(account);
        ThrowIfError(SecKeychainAddGenericPassword(
            IntPtr.Zero,
            (uint)service.Length, service,
            (uint)accountBytes.Length, accountBytes,
            (uint)passwordBytes.Length, passwordBytes,
            IntPtr.Zero));
    }

    private static void Delete(string account)
    {
        var status = Find(account, out _, out var data, out var item);
        if (status == ErrSecItemNotFound) return;
        ThrowIfError(status);

        try { ThrowIfError(SecKeychainItemDelete(item)); }
        finally { Release(data, item); }
    }

    private static int Find(string account, out uint length, out IntPtr data, out IntPtr item)
    {
        var service = Encoding.UTF8.GetBytes(ServiceName);
        var accountBytes = Encoding.UTF8.GetBytes(account);
        return SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)service.Length, service,
            (uint)accountBytes.Length, accountBytes,
            out length, out data, out item);
    }

    private static void Release(IntPtr data, IntPtr item)
    {
        if (data != IntPtr.Zero) SecKeychainItemFreeContent(IntPtr.Zero, data);
        if (item != IntPtr.Zero) CFRelease(item);
    }

    private static void ThrowIfError(int status)
    {
        if (status != ErrSecSuccess)
            throw new InvalidOperationException($"The macOS Keychain returned error {status}.");
    }
}
