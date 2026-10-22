namespace Synchro.Core.Notifications;

/// <summary>Shows a notification outside the app (macOS Notification Center). Implemented per platform.</summary>
public interface INotifier
{
    /// <summary>Asks the OS for permission once, at start-up. Safe to call when notifications are unavailable.</summary>
    void Initialize();

    /// <summary>Shows a notification. Silently does nothing when the OS doesn't allow it.</summary>
    void Notify(string title, string message);
}
