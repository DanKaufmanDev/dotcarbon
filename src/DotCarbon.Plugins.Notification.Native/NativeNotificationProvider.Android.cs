#if ANDROID
using Android.App;
using Android.Content;
using DotCarbon.Core.Runtime;
using DotCarbon.Plugins.Notification;

namespace DotCarbon.Plugins.Notification.Native;

/// <summary>
/// Posts a local notification via Android's <see cref="NotificationManager"/>, creating a default
/// channel on API 26+. (On API 33+ the app must also hold the POST_NOTIFICATIONS runtime permission.)
/// </summary>
internal sealed class NativeNotificationProvider : INotificationProvider
{
    private const string ChannelId = "carbon_default";
    private readonly AppHandle _app;
    private int _nextId = 1;

    public NativeNotificationProvider(AppHandle app) => _app = app;

    private Context Context => _app.PlatformNativeHandle as Context ?? Application.Context;

    public Task Send(SendNotificationArgs args)
    {
        var context = Context;
        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;

        global::Android.App.Notification.Builder builder;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            manager.CreateNotificationChannel(
                new NotificationChannel(ChannelId, "Notifications", NotificationImportance.Default));
            builder = new global::Android.App.Notification.Builder(context, ChannelId);
        }
        else
        {
#pragma warning disable CA1422, CS0618 // legacy builder for API < 26
            builder = new global::Android.App.Notification.Builder(context);
#pragma warning restore CA1422, CS0618
        }

        builder
            .SetContentTitle(args.Title)
            .SetContentText(args.Body)
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetAutoCancel(true);
        if (!string.IsNullOrEmpty(args.Subtitle)) builder.SetSubText(args.Subtitle);

        manager.Notify(_nextId++, builder.Build());
        return Task.CompletedTask;
    }
}
#endif
