using System.Runtime.InteropServices;

namespace Synchro.Platform.MacOS;

/// <summary>
/// The few Objective-C runtime calls needed to talk to UserNotifications. objc_msgSend is bound once per signature
/// (on Apple Silicon it must not be called as a variadic function).
/// </summary>
internal static class ObjCInterop
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string LibSystem = "/usr/lib/libSystem.dylib";

    public const int RtldNow = 0x2;
    public static readonly IntPtr RtldDefault = new(-2);

    [DllImport(LibSystem)]
    public static extern IntPtr dlopen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);

    [DllImport(LibSystem)]
    public static extern IntPtr dlsym(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string symbol);

    [DllImport(ObjC)]
    public static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC)]
    public static extern IntPtr objc_getProtocol([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC)]
    public static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjC)]
    public static extern IntPtr objc_allocateClassPair(IntPtr superclass, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint extraBytes);

    [DllImport(ObjC)]
    public static extern void objc_registerClassPair(IntPtr cls);

    [DllImport(ObjC)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr implementation, [MarshalAs(UnmanagedType.LPUTF8Str)] string types);

    [DllImport(ObjC)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool class_addProtocol(IntPtr cls, IntPtr protocol);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, nuint arg1, IntPtr arg2);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendString(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);

    public static IntPtr Class(string name) => objc_getClass(name);
    public static IntPtr Sel(string name) => sel_registerName(name);

    /// <summary>An autoreleased NSString.</summary>
    public static IntPtr NSString(string text) => SendString(Class("NSString"), Sel("stringWithUTF8String:"), text);

    /// <summary>
    /// A block literal (Apple's Blocks ABI) with no captured state. Global blocks are never copied or freed, so the
    /// memory is allocated once and kept for the life of the process.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public IntPtr Isa;
        public int Flags;
        public int Reserved;
        public IntPtr Invoke;
        public IntPtr Descriptor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
    }

    private const int BlockIsGlobal = 1 << 28;

    public static IntPtr CreateGlobalBlock(IntPtr invoke)
    {
        var descriptor = Marshal.AllocHGlobal(Marshal.SizeOf<BlockDescriptor>());
        Marshal.StructureToPtr(new BlockDescriptor { Reserved = 0, Size = (nuint)Marshal.SizeOf<BlockLiteral>() }, descriptor, false);

        var block = Marshal.AllocHGlobal(Marshal.SizeOf<BlockLiteral>());
        Marshal.StructureToPtr(new BlockLiteral
        {
            Isa = dlsym(RtldDefault, "_NSConcreteGlobalBlock"),
            Flags = BlockIsGlobal,
            Invoke = invoke,
            Descriptor = descriptor,
        }, block, false);
        return block;
    }

    /// <summary>The function pointer stored in a block literal, at the same offset for every block.</summary>
    public static IntPtr BlockInvokePointer(IntPtr block) => Marshal.ReadIntPtr(block, IntPtr.Size + 8);
}
