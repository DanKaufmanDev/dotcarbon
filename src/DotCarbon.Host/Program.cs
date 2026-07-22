using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;
using DotCarbon.Host.Desktop;
using DotCarbon.Plugins.FileSystem;
using DotCarbon.Plugins.Dialog;
using DotCarbon.Plugins.Shell;
using DotCarbon.Plugins.Window;
using DotCarbon.Plugins.Notification;
using DotCarbon.Plugins.Clipboard;

var config = ConfigLoader.Load();

CarbonApp.Create(config)
    .UseDesktop()
    .WithPlugin<FileSystemPlugin>()
    // Dialogs come from the host (ICarbonDialogs) now, so this registers like any other plugin.
    .WithPlugin<DialogPlugin>()
    .WithPlugin<ShellPlugin>()
    .WithPlugin<WindowPlugin>()
    .WithPlugin<NotificationPlugin>()
    .WithPlugin<ClipboardPlugin>()
    .Run();
