using System.Diagnostics;
using System.Runtime.Versioning;
using Synchro.Core.Notifications;
using Tmds.DBus.Protocol;

namespace Synchro.Platform.Linux;

/// <summary>
/// Desktop notifications through the freedesktop.org Notifications service on the session bus, which GNOME, KDE and
/// most other desktops provide. The icon and the "desktop-entry" hint name the app's installed .desktop file, so the
/// desktop shows the app's icon and groups its notifications.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class DesktopNotifier : INotifier
{
    /// <summary>The .desktop file and icon name the install script uses (the app identifier).</summary>
    public const string DesktopEntry = "nl.vinch.synchromesh";

    private const string Service = "org.freedesktop.Notifications";
    private const string ObjectPath = "/org/freedesktop/Notifications";

    // Nothing to ask for: the freedesktop service has no permission prompt.
    public void Initialize()
    {
    }

    public void Notify(string title, string message) => _ = NotifyAsync(title, message);

    private static async Task NotifyAsync(string title, string message)
    {
        try
        {
            var connection = DBusConnection.Session;
            var writer = connection.GetMessageWriter();
            // Notify(app_name, replaces_id, app_icon, summary, body, actions, hints, expire_timeout) -> id
            writer.WriteMethodCallHeader(destination: Service, path: ObjectPath, @interface: Service, member: "Notify",
                signature: "susssasa{sv}i");
            writer.WriteString("Synchromesh");
            writer.WriteUInt32(0);
            writer.WriteString(DesktopEntry);
            writer.WriteString(title);
            writer.WriteString(message);
            writer.WriteArrayEnd(writer.WriteArrayStart(DBusType.String));
            var hints = writer.WriteDictionaryStart();
            writer.WriteDictionaryEntryStart();
            writer.WriteString("desktop-entry");
            writer.WriteVariantString(DesktopEntry);
            writer.WriteDictionaryEnd(hints);
            writer.WriteInt32(-1); // the desktop's default display time
            await connection.CallMethodAsync(writer.CreateMessage(), (Message m, object? _) => m.GetBodyReader().ReadUInt32());
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Couldn't show a notification: {ex.Message}");
        }
    }
}
