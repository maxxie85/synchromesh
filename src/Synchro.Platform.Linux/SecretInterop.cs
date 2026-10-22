using System.Runtime.InteropServices;

namespace Synchro.Platform.Linux;

/// <summary>
/// The parts of libsecret and GLib the keyring store needs. Only the non-variadic "v" functions are used: they take
/// the attributes as a GHashTable, and variadic C functions can't be called safely from .NET.
/// </summary>
internal static class SecretInterop
{
    private const string Secret = "libsecret-1.so.0";
    private const string GLib = "libglib-2.0.so.0";

    [DllImport(Secret)]
    public static extern IntPtr secret_schema_newv(IntPtr name, int flags, IntPtr attributeNamesAndTypes);

    [DllImport(Secret)]
    public static extern void secret_schema_unref(IntPtr schema);

    [DllImport(Secret)]
    [return: MarshalAs(UnmanagedType.I4)]
    public static extern int secret_password_storev_sync(
        IntPtr schema, IntPtr attributes, IntPtr collection, IntPtr label, IntPtr password, IntPtr cancellable, out IntPtr error);

    [DllImport(Secret)]
    public static extern IntPtr secret_password_lookupv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);

    [DllImport(Secret)]
    [return: MarshalAs(UnmanagedType.I4)]
    public static extern int secret_password_clearv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);

    /// <summary>Wipes and frees a password returned by lookup.</summary>
    [DllImport(Secret)]
    public static extern void secret_password_free(IntPtr password);

    [DllImport(GLib)]
    public static extern IntPtr g_hash_table_new(IntPtr hashFunc, IntPtr keyEqualFunc);

    [DllImport(GLib)]
    [return: MarshalAs(UnmanagedType.I4)]
    public static extern int g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);

    [DllImport(GLib)]
    public static extern void g_hash_table_unref(IntPtr table);

    [DllImport(GLib)]
    public static extern void g_error_free(IntPtr error);

    [DllImport(GLib)]
    public static extern IntPtr g_quark_to_string(uint quark);

    /// <summary>GLib's string hash and equality, passed to g_hash_table_new as function pointers.</summary>
    public static (IntPtr Hash, IntPtr Equal) StringFunctions()
    {
        var glib = NativeLibrary.Load(GLib);
        return (NativeLibrary.GetExport(glib, "g_str_hash"), NativeLibrary.GetExport(glib, "g_str_equal"));
    }

    /// <summary>A GError's domain, code and message (struct { GQuark domain; gint code; gchar *message; }), then frees it.</summary>
    public static GError TakeError(IntPtr error)
    {
        if (error == IntPtr.Zero) return new GError("", 0, "unknown error");
        var domain = Marshal.PtrToStringUTF8(g_quark_to_string((uint)Marshal.ReadInt32(error, 0))) ?? "";
        var code = Marshal.ReadInt32(error, 4);
        var message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8)) ?? "unknown error";
        g_error_free(error);
        return new GError(domain, code, message);
    }

    public sealed record GError(string Domain, int Code, string Message)
    {
        private const string DBusDomain = "g-dbus-error-quark";
        private const int ServiceUnknown = 2;   // G_DBUS_ERROR_SERVICE_UNKNOWN: "The name is not activatable"
        private const int NameHasNoOwner = 3;   // G_DBUS_ERROR_NAME_HAS_NO_OWNER
        private const int NoServer = 11;        // G_DBUS_ERROR_NO_SERVER: no session bus

        /// <summary>
        /// Nothing on the session bus offers the Secret Service, or there is no session bus: the keyring is missing,
        /// not merely locked or refused (those must stay ordinary errors).
        /// </summary>
        public bool MeansNoKeyring =>
            (Domain == DBusDomain && Code is ServiceUnknown or NameHasNoOwner or NoServer)
            || Message.Contains("not activatable", StringComparison.OrdinalIgnoreCase)
            || Message.Contains("org.freedesktop.secrets was not provided", StringComparison.OrdinalIgnoreCase)
            || Message.Contains("autolaunch D-Bus", StringComparison.OrdinalIgnoreCase);
    }
}
