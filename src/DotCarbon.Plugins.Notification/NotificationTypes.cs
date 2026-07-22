namespace DotCarbon.Plugins.Notification;

public record SendNotificationArgs(
    string Title,
    string Body,
    string? Subtitle = null
);

/// <summary>
/// Delivers a local notification on the current platform. The desktop plugin ships a subprocess-based
/// implementation; a mobile app registers a native one (Android <c>NotificationManager</c> / iOS
/// <c>UNUserNotificationCenter</c>) via <c>app.UseNotifications()</c> from the
/// <c>DotCarbon.Plugins.Notification.Native</c> package.
/// </summary>
public interface INotificationProvider
{
    Task Send(SendNotificationArgs args);
}