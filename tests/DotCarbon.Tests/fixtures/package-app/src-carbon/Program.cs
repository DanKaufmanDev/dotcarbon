using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;
using DotCarbon.Host.Desktop;

var config = ConfigLoader.Load();

CarbonApp.Create(config)
    .UseDesktop()
    .UseTray(tray => tray
        .SetTitle("●")
        .AddItem("Show", () => { })
        .AddSeparator()
        .AddItem("Quit", () => Environment.Exit(0)))
    .UseMenu(menu => menu
        .AddMenu("App", app => app
            .AddItem("About", () => { })
            .AddSeparator()
            .AddItem("Quit", () => Environment.Exit(0), "CmdOrCtrl+Q")))
    .Run();
