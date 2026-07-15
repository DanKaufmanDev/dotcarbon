using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;
using DotCarbon.Host.Desktop;

var config = ConfigLoader.Load();

CarbonApp.Create(config)
    .UseDesktop()
    .UseTray(
        tray => tray
            .SetTitle("●")
            .AddItem("Show", () => { })
            .AddSeparator()
            .AddItem("Quit", () => Environment.Exit(0)),
        // Task 2.3: exercise runtime mutation so the smoke proves the setters actually run.
        onReady: tray =>
        {
            tray.SetTitle("◆");
            tray.SetTooltip("Carbon smoke");
            Console.WriteLine("[Carbon] Tray mutation applied.");
            Console.Out.Flush();
        })
    .UseMenu(
        menu => menu
            .AddMenu("App", app => app
                .AddItem("About", () => { }, id: "about")
                .AddCheckItem("Verbose", () => { }, isChecked: false, id: "verbose")
                .AddSeparator()
                .AddItem("Quit", () => Environment.Exit(0), "CmdOrCtrl+Q")),
        // Task 2.4: exercise runtime mutation so the smoke proves the setters actually run.
        onReady: menu =>
        {
            menu.SetEnabled("about", false);
            menu.SetChecked("verbose", true);
            menu.SetLabel("about", "About Carbon");
            Console.WriteLine("[Carbon] Menu mutation applied.");
            Console.Out.Flush();
        })
    .Run();
