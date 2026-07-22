#if IOS
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Notification;
using UserNotifications;

namespace DotCarbon.Plugins.Notification.Native;

/// <summary>Posts a local notification via iOS's <see cref="UNUserNotificationCenter"/>, requesting authorization first.</summary>
internal sealed class NativeNotificationProvider : INotificationProvider
{
    public NativeNotificationProvider(AppHandle app) { }

    public async Task Send(SendNotificationArgs args)
    {
        var center = UNUserNotificationCenter.Current;

        var (granted, _) = await center.RequestAuthorizationAsync(
            UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound | UNAuthorizationOptions.Badge);
        if (!granted) return;

        var content = new UNMutableNotificationContent
        {
            Title = args.Title,
            Body = args.Body,
        };
        if (!string.IsNullOrEmpty(args.Subtitle)) content.Subtitle = args.Subtitle;

        // A tiny non-zero delay is required; iOS drops a request with a zero-interval trigger.
        var trigger = UNTimeIntervalNotificationTrigger.CreateTrigger(0.1, repeats: false);
        var request = UNNotificationRequest.FromIdentifier(Guid.NewGuid().ToString(), content, trigger);
        await center.AddNotificationRequestAsync(request);
    }
}
#endif
