using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;
using DotCarbon.Host.Desktop;

var config = ConfigLoader.Load();

CarbonApp.Create(config)
    .UseDesktop()
    .UseTray(
        tray => tray
            .SetTitle("●")
            // Task 2.5: a real image icon (PNG), resolved next to the app.
            .SetIcon(Path.Combine(AppContext.BaseDirectory, "icons", "tray.png"))
            .AddItem("Show", () => { })
            // Task 2.7: nested tray submenu (two levels deep).
            .AddSubmenu("More", more => more
                .AddItem("Docs", () => { })
                .AddSubmenu("Deeper", deeper => deeper
                    .AddItem("Nested", () => { })))
            .AddSeparator()
            .AddItem("Quit", () => Environment.Exit(0))
            // Task 2.8: pointer events on the icon. Registering a handler is what makes macOS hand us
            // the button instead of letting AppKit own it, so the smoke covers that path.
            // Move fires continuously while the pointer is over the icon, so it is handled but not
            // logged — otherwise one pass over the menu bar floods the smoke log.
            .OnEvent(e =>
            {
                if (e.Kind == CarbonTrayEventKind.Move) return;
                Console.WriteLine(
                    $"[Carbon] Tray event: {e.Kind} {e.Button} {e.ButtonState} " +
                    $"at ({e.Position.X},{e.Position.Y}) rect=({e.Rect.X},{e.Rect.Y},{e.Rect.Width},{e.Rect.Height})");
            })
            .OnEvent("tray:pointer")
            .ShowMenuOnLeftClick(true),
        // Task 2.3: exercise runtime mutation so the smoke proves the setters actually run.
        onReady: tray =>
        {
            tray.SetTitle("◆");
            tray.SetTooltip("Carbon smoke");
            tray.SetIcon(Path.Combine(AppContext.BaseDirectory, "icons", "tray.png"));
            Console.WriteLine("[Carbon] Tray mutation applied.");
            Console.Out.Flush();
        })
    .UseMenu(
        menu => menu
            .AddMenu("App", app => app
                .AddItem("About", () => { }, id: "about")
                .AddCheckItem("Verbose", () => { }, isChecked: false, id: "verbose")
                // Task 2.7: nested app-menu submenu.
                .AddSubmenu("Recent", recent => recent
                    .AddItem("Project A", () => { })
                    .AddSubmenu("Archived", archived => archived
                        .AddItem("Old Project", () => { })))
                .AddSeparator()
                .AddItem("Quit", () => Environment.Exit(0), "CmdOrCtrl+Q"))
            // Task 2.6: predefined roles, including a relabelled one and the two roles that are
            // implemented on every platform.
            .AddMenu("Edit", edit => edit
                .AddPredefined(CarbonMenuRole.Undo)
                .AddPredefined(CarbonMenuRole.Redo)
                .AddSeparator()
                .AddPredefined(CarbonMenuRole.Cut)
                .AddPredefined(CarbonMenuRole.Copy)
                .AddPredefined(CarbonMenuRole.Paste)
                .AddPredefined(CarbonMenuRole.SelectAll, "Select Everything")
                .AddSeparator()
                .AddPredefined(CarbonMenuRole.Minimize)
                .AddPredefined(CarbonMenuRole.CloseWindow)),
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
