using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Notification;
using Microsoft.Extensions.DependencyInjection;

namespace DotCarbon.Plugins.Notification.Native;

/// <summary>
/// Registers the platform-native <see cref="INotificationProvider"/> so <c>NotificationPlugin</c> posts
/// real local notifications on Android/iOS. Call before <c>Start()</c>:
/// <code>app.UseNotifications().UsePlugin&lt;NotificationPlugin&gt;();</code>
/// </summary>
public static class NotificationNativeExtensions
{
    public static CarbonApp UseNotifications(this CarbonApp app)
    {
        app.ConfigureServices(services =>
            services.AddSingleton<INotificationProvider>(sp =>
                new NativeNotificationProvider(sp.GetRequiredService<AppHandle>())));
        return app;
    }
}
