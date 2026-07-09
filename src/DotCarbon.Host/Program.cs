using DotCarbon.Core.Config;
using DotCarbon.Core.Host;
using DotCarbon.Core.Plugins;
using DotCarbon.Plugins.FileSystem;
using DotCarbon.Plugins.Dialog;
using DotCarbon.Plugins.Shell;
using DotCarbon.Plugins.Window;
using DotCarbon.Plugins.Notification;
using DotCarbon.Plugins.Clipboard;

var config = ConfigLoader.Load();

new CarbonHost(config)
    .WithPlugin(new FileSystemPlugin())
    .WithPlugin(window => new DialogPlugin(window))
    .WithPlugin(new ShellPlugin())
    .WithPlugin(window => new WindowPlugin(window))
    .WithPlugin(new NotificationPlugin())
    .WithPlugin(new ClipboardPlugin())
    .Run();