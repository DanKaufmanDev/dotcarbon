using DotCarbon.Core.Bridge;
using DotCarbon.Core.Config;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using DotCarbon.Host.Desktop;

var config = ConfigLoader.Load();

CarbonApp.Create(config)
    .UseDesktop()
    .UsePlugin<AppCommands>()
    // A system-tray icon with a menu. Selecting an item runs C#; "Quit" exits the app.
    .UseTray(tray => tray
        .SetTitle("●")
        .SetIcon(Path.Combine(AppContext.BaseDirectory, "icons", "tray.png"))
        .AddItem("Say hello", () => Console.WriteLine("[tray] hello from C#"))
        .AddSeparator()
        .AddItem("Quit", () => Environment.Exit(0))
        .ShowMenuOnLeftClick(true))
    // A native application menu (the macOS menu bar / the window menu on Windows and Linux).
    .UseMenu(menu => menu
        .AddMenu("App", app => app
            .AddItem("About", () => Console.WriteLine("[menu] Tray Menu example"))
            .AddSeparator()
            .AddItem("Quit", () => Environment.Exit(0), "CmdOrCtrl+Q"))
        .AddMenu("Edit", edit => edit
            .AddPredefined(CarbonMenuRole.Copy)
            .AddPredefined(CarbonMenuRole.Paste)))
    .Run();

public record GreetRequest(string Name);

public partial class AppCommands : IPlugin
{
    public string Namespace => "app";

    /// <summary>Greets by name — called from the frontend to prove the bridge works.</summary>
    [CarbonCommand("greet")]
    public string Greet(GreetRequest req) => $"Hello, {req.Name}! Now try the tray icon and the menu bar.";
}
