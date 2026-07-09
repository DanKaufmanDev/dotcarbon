namespace DotCarbon.Plugins.Notification;

public record SendNotificationArgs(
    string Title,
    string Body,
    string? Subtitle = null
);