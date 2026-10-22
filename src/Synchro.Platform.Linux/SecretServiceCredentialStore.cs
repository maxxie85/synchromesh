using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Synchro.Core.Servers;
using static Synchro.Platform.Linux.SecretInterop;

namespace Synchro.Platform.Linux;

/// <summary>
/// Stores server passwords in the desktop keyring through the Secret Service (GNOME Keyring, KWallet, KeePassXC),
/// via libsecret. One item per server profile, found by the attributes service=Synchromesh and profile=id.
/// The calls can wait for the user to unlock the keyring, so they run off the UI thread.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class SecretServiceCredentialStore : ICredentialStore
{
    private const string SchemaName = "nl.vinch.synchromesh.Password";
    private const string ServiceName = "Synchromesh";
    private const string NoKeyring =
        "No keyring was found to keep the password. Install and unlock one that offers the Secret Service " +
        "(GNOME Keyring, KWallet or KeePassXC), or leave the password empty.";

    public string Name => "the keyring";

    public Task<string?> GetPasswordAsync(Guid profileId, CancellationToken ct = default)
        => Task.Run(() => Run(profileId, (schema, attributes) =>
        {
            var password = secret_password_lookupv_sync(schema, attributes, IntPtr.Zero, out var error);
            if (error != IntPtr.Zero) throw Failure("read", error);
            if (password == IntPtr.Zero) return null;

            try { return Marshal.PtrToStringUTF8(password); }
            finally { secret_password_free(password); }
        }), ct);

    public Task SetPasswordAsync(Guid profileId, string password, CancellationToken ct = default)
        => Task.Run(() => Run<object?>(profileId, (schema, attributes) =>
        {
            using var label = new Utf8($"Synchromesh server {profileId:D}");
            using var secret = new Utf8(password);
            // A null collection means the user's default keyring ("login" on GNOME).
            if (secret_password_storev_sync(schema, attributes, IntPtr.Zero, label.Pointer, secret.Pointer, IntPtr.Zero, out var error) == 0)
                throw Failure("save", error);
            return null;
        }), ct);

    public Task DeleteAsync(Guid profileId, CancellationToken ct = default)
        => Task.Run(() => Run<object?>(profileId, (schema, attributes) =>
        {
            // False without an error just means there was nothing to remove.
            secret_password_clearv_sync(schema, attributes, IntPtr.Zero, out var error);
            if (error != IntPtr.Zero) throw Failure("remove", error);
            return null;
        }), ct);

    private static T Run<T>(Guid profileId, Func<IntPtr, IntPtr, T> action)
    {
        try
        {
            var (hash, equal) = StringFunctions();
            using var schemaText = new Utf8(SchemaName);
            using var serviceKey = new Utf8("service");
            using var profileKey = new Utf8("profile");
            using var service = new Utf8(ServiceName);
            using var profile = new Utf8(profileId.ToString("D"));

            // Both attributes are strings: SECRET_SCHEMA_ATTRIBUTE_STRING is 0, stored as the hash table value.
            var types = g_hash_table_new(hash, equal);
            g_hash_table_insert(types, serviceKey.Pointer, IntPtr.Zero);
            g_hash_table_insert(types, profileKey.Pointer, IntPtr.Zero);
            var schema = secret_schema_newv(schemaText.Pointer, 0, types);
            g_hash_table_unref(types);

            var attributes = g_hash_table_new(hash, equal);
            g_hash_table_insert(attributes, serviceKey.Pointer, service.Pointer);
            g_hash_table_insert(attributes, profileKey.Pointer, profile.Pointer);
            try
            {
                return action(schema, attributes);
            }
            finally
            {
                g_hash_table_unref(attributes);
                secret_schema_unref(schema);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new KeyringUnavailableException(NoKeyring, ex);
        }
    }

    // Typical causes: no Secret Service on the bus (then the password file can take over), or the user dismissed the
    // unlock prompt.
    private static Exception Failure(string verb, IntPtr error)
    {
        var gerror = TakeError(error);
        var message = $"The keyring couldn't {verb} the password: {gerror.Message.TrimEnd('.')}. {NoKeyring}";
        return gerror.MeansNoKeyring ? new KeyringUnavailableException(message) : new InvalidOperationException(message);
    }

    /// <summary>A NUL-terminated UTF-8 copy for the duration of a call.</summary>
    private sealed class Utf8(string text) : IDisposable
    {
        public IntPtr Pointer { get; } = Marshal.StringToCoTaskMemUTF8(text);

        public void Dispose() => Marshal.FreeCoTaskMem(Pointer);
    }
}
