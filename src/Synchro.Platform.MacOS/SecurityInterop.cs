using System.Runtime.InteropServices;

namespace Synchro.Platform.MacOS;

/// <summary>
/// Minimal P/Invoke surface for the Keychain "generic password" API.
/// These SecKeychain* functions are deprecated by Apple but still shipped and the simplest
/// to call from .NET. A later step can move to SecItem* (CFDictionary-based) if needed.
/// </summary>
internal static class SecurityInterop
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public const int ErrSecSuccess = 0;
    public const int ErrSecDuplicateItem = -25299;
    public const int ErrSecItemNotFound = -25300;

    [DllImport(Security)]
    public static extern int SecKeychainAddGenericPassword(
        IntPtr keychain,
        uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName,
        uint passwordLength, byte[] passwordData,
        IntPtr itemRef);

    [DllImport(Security)]
    public static extern int SecKeychainFindGenericPassword(
        IntPtr keychainOrArray,
        uint serviceNameLength, byte[] serviceName,
        uint accountNameLength, byte[] accountName,
        out uint passwordLength, out IntPtr passwordData,
        out IntPtr itemRef);

    [DllImport(Security)]
    public static extern int SecKeychainItemModifyAttributesAndData(
        IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

    [DllImport(Security)]
    public static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(Security)]
    public static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport(CoreFoundation)]
    public static extern void CFRelease(IntPtr cf);
}
