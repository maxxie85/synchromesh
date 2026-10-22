namespace Synchro.Platform.MacOS;

/// <summary>
/// The standard app-menu actions. Avalonia adds them only together with Services, so the app turns its defaults off
/// (<c>DisableDefaultApplicationMenuItems</c>) and calls NSApplication itself. Call on the main thread.
/// </summary>
public static class MacApplicationMenu
{
    public static void Hide() => Send("hide:");

    public static void HideOthers() => Send("hideOtherApplications:");

    public static void ShowAll() => Send("unhideAllApplications:");

    private static void Send(string selector)
    {
        var app = ObjCInterop.Send(ObjCInterop.Class("NSApplication"), ObjCInterop.Sel("sharedApplication"));
        ObjCInterop.Send(app, ObjCInterop.Sel(selector), IntPtr.Zero);
    }
}
