using System.Diagnostics;
using System.Security;
using Microsoft.Win32;
using Synchro.Core.Notifications;
using Windows.UI.Notifications;

namespace Synchro.Platform.Windows;

/// <summary>
/// Windows toast notifications for an unpackaged app: the app registers its own identity (AppUserModelID) under
/// HKCU\Software\Classes\AppUserModelId with its display name and icon, and uses that ID for the process and the
/// toasts. No installer step or shortcut is needed, so this also works for a copy run from anywhere.
/// </summary>
public sealed class WindowsNotifier : INotifier
{
    public const string AppId = "nl.vinch.synchromesh";
    private const string DisplayName = "Synchromesh";

    private ToastNotifier? _notifier;

    public void Initialize()
    {
        if (_notifier is not null) return;

        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}"))
            {
                key.SetValue("DisplayName", DisplayName);
                var icon = Path.Combine(AppContext.BaseDirectory, "AppIcon.png");
                if (File.Exists(icon)) key.SetValue("IconUri", icon);
            }

            // Also groups the taskbar button under this identity; must happen before the first window opens.
            WindowsInterop.SetCurrentProcessExplicitAppUserModelID(AppId);
            _notifier = ToastNotificationManager.CreateToastNotifier(AppId);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException
                                       or System.Runtime.InteropServices.COMException)
        {
            Trace.TraceWarning($"Notifications unavailable: {ex.Message}");
        }
    }

    public void Notify(string title, string message)
    {
        if (_notifier is null) return;

        try
        {
            var xml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var texts = xml.GetElementsByTagName("text");
            texts[0].AppendChild(xml.CreateTextNode(title));
            texts[1].AppendChild(xml.CreateTextNode(message));
            _notifier.Show(new ToastNotification(xml));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            Trace.TraceWarning($"Couldn't show a notification: {ex.Message}");
        }
    }
}
