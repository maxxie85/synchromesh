using System.Diagnostics;
using System.Runtime.InteropServices;
using Synchro.Core.Notifications;
using static Synchro.Platform.MacOS.ObjCInterop;

namespace Synchro.Platform.MacOS;

/// <summary>
/// macOS Notification Center through the UserNotifications framework.
///
/// Only works inside the .app bundle: UNUserNotificationCenter needs a bundle identifier and throws an Objective-C
/// exception without one, which would end the process. So under `dotnet run` this notifier does nothing.
/// Whether notifications show is up to the user (System Settings > Notifications), as for any Mac app.
/// </summary>
public sealed unsafe class MacNotifier : INotifier
{
    private const string UserNotifications = "/System/Library/Frameworks/UserNotifications.framework/UserNotifications";

    // UNAuthorizationOptions and UNNotificationPresentationOptions.
    private const nuint AuthorizationSoundAndAlert = (1 << 1) | (1 << 2);
    private const nuint PresentBannerListAndSound = (1 << 4) | (1 << 3) | (1 << 1);

    private static IntPtr _authorizationBlock;
    private static IntPtr _delegate;

    private IntPtr _center;

    public void Initialize()
    {
        if (_center != IntPtr.Zero) return;

        try
        {
            if (!IsInBundle())
            {
                Trace.TraceInformation("Not running from the .app bundle: notifications are off.");
                return;
            }

            if (dlopen(UserNotifications, RtldNow) == IntPtr.Zero || Class("UNUserNotificationCenter") == IntPtr.Zero)
                return;

            var center = Send(Class("UNUserNotificationCenter"), Sel("currentNotificationCenter"));
            if (center == IntPtr.Zero) return;

            Send(center, Sel("setDelegate:"), CreateDelegate());

            _authorizationBlock = CreateGlobalBlock((IntPtr)(delegate* unmanaged<IntPtr, byte, IntPtr, void>)&OnAuthorization);
            Send(center, Sel("requestAuthorizationWithOptions:completionHandler:"), AuthorizationSoundAndAlert, _authorizationBlock);

            _center = center;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Trace.TraceWarning($"Notifications unavailable: {ex.Message}");
        }
    }

    public void Notify(string title, string message)
    {
        if (_center == IntPtr.Zero) return;

        var content = Send(Send(Class("UNMutableNotificationContent"), Sel("alloc")), Sel("init"));
        Send(content, Sel("setTitle:"), NSString(title));
        Send(content, Sel("setBody:"), NSString(message));
        Send(content, Sel("setSound:"), Send(Class("UNNotificationSound"), Sel("defaultSound")));

        var identifier = Send(Send(Class("NSUUID"), Sel("UUID")), Sel("UUIDString"));
        var request = Send(Class("UNNotificationRequest"), Sel("requestWithIdentifier:content:trigger:"), identifier, content, IntPtr.Zero);
        Send(content, Sel("release")); // the request keeps its own copy

        Send(_center, Sel("addNotificationRequest:withCompletionHandler:"), request, IntPtr.Zero);
    }

    private static bool IsInBundle()
    {
        var bundle = Send(Class("NSBundle"), Sel("mainBundle"));
        return bundle != IntPtr.Zero && Send(bundle, Sel("bundleIdentifier")) != IntPtr.Zero;
    }

    /// <summary>
    /// Without a delegate, macOS hides notifications while the app is in front. This one asks for a banner anyway,
    /// so a finished download is announced whether or not the window has focus.
    /// </summary>
    private static IntPtr CreateDelegate()
    {
        if (_delegate != IntPtr.Zero) return _delegate;

        var cls = objc_allocateClassPair(Class("NSObject"), "TmNotificationCenterDelegate", 0);
        class_addMethod(cls, Sel("userNotificationCenter:willPresentNotification:withCompletionHandler:"),
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&WillPresent, "v@:@@@?");
        var protocol = objc_getProtocol("UNUserNotificationCenterDelegate");
        if (protocol != IntPtr.Zero) class_addProtocol(cls, protocol);
        objc_registerClassPair(cls);

        // Kept for the life of the process: the center holds its delegate weakly.
        _delegate = Send(Send(cls, Sel("alloc")), Sel("init"));
        return _delegate;
    }

    [UnmanagedCallersOnly]
    private static void WillPresent(IntPtr self, IntPtr selector, IntPtr center, IntPtr notification, IntPtr completionHandler)
    {
        var invoke = (delegate* unmanaged<IntPtr, nuint, void>)BlockInvokePointer(completionHandler);
        invoke(completionHandler, PresentBannerListAndSound);
    }

    [UnmanagedCallersOnly]
    private static void OnAuthorization(IntPtr block, byte granted, IntPtr error)
    {
        if (granted == 0)
            Trace.TraceInformation("Notifications were not allowed; turn them on in System Settings > Notifications.");
    }
}
