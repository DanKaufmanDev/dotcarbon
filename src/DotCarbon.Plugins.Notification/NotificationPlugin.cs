using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace DotCarbon.Plugins.Notification;

[CarbonPlugin("Notifications", description: "Send native notifications (desktop and mobile).")]
[CarbonPluginPlatform("desktop", "android", "ios")]
[CarbonPermission("notification:default", "Allow all notification commands.", Commands = new[] { "notification:*" })]
public partial class NotificationPlugin : IPlugin
{
    private readonly INotificationProvider _provider;

    public NotificationPlugin(AppHandle app)
        : this(app.Services.GetService<INotificationProvider>() ?? new DesktopNotificationProvider()) { }

    // Injection seam for tests and for the native binding.
    internal NotificationPlugin(INotificationProvider provider) => _provider = provider;

    public string Namespace => "notification";

    [CarbonCommand("send")]
    public Task Send(SendNotificationArgs args) => _provider.Send(args);
}
