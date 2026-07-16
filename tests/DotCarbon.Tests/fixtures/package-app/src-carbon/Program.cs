using System.Text.Json.Serialization;
using DotCarbon.Core.Config;
using DotCarbon.Core.Runtime;
using DotCarbon.Host.Desktop;

var config = ConfigLoader.Load();

CarbonApp.Create(config)
    .UseDesktop()
    // Task 2.10: ui/dist/app.js drives the tray:*/menu:* commands over the bridge and reports back.
    // Those invokes only resolve if the native side accepted them, so this marker means the whole
    // JS -> bridge -> plugin -> native path ran.
    .Setup(handle =>
    {
        handle.Events.Listen(
            new CarbonEventName<string>("smoke:ui_ok"), SmokeJsonContext.Default.String,
            e =>
            {
                Console.WriteLine($"[[CARBON_JS_UI_OK]] {e.Payload}");
                Console.Out.Flush();
            });
        handle.Events.Listen(
            new CarbonEventName<string>("smoke:ui_err"), SmokeJsonContext.Default.String,
            e =>
            {
                Console.Error.WriteLine($"[[CARBON_JS_UI_ERR]] {e.Payload}");
                Console.Error.Flush();
            });
    })
    // Task 3.1: drive show/hide/focus on the real native window and read the OS state back, so the
    // smoke proves these actually move the window rather than just returning. Pure native — no
    // webview needed — so unlike the JS path this runs on the macOS CI runner too. The window ops
    // touch AppKit/GTK, which are main-thread-only, so this runs through Photino.Invoke (the UI
    // thread) rather than a threadpool task — the same thread the bridge would call them on.
    .OnLifecycle(e =>
    {
        if (e.Kind != CarbonLifecycleEventKind.WindowCreated || e.Window is not { } window) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(800); // let the window finish laying out
            window.Photino().Invoke(() =>
            {
                var view = window.Native;
                var shown = view.IsVisible;
                view.Hide();
                var hidden = view.IsVisible;
                view.Show();
                var reshown = view.IsVisible;
                view.SetFocus();
                Console.WriteLine(
                    $"[[CARBON_WIN]] visible_initial={shown} hidden={hidden} reshown={reshown} focused={view.IsFocused}");

                // Task 3.2: geometry. inner (content) should be shorter than outer (frame) by the
                // title bar; positions land at sane screen coordinates.
                var (iw, ih) = view.GetInnerSize();
                var (ow, oh) = view.GetOuterSize();
                var (ix, iy) = view.GetInnerPosition();
                var (ox, oy) = view.GetOuterPosition();
                view.SetMinSize(640, 480);
                view.SetMaxSize(1280, 960);
                var photino = window.Photino();
                Console.WriteLine(
                    $"[[CARBON_GEO]] inner={iw}x{ih}@{ix},{iy} outer={ow}x{oh}@{ox},{oy} photino={view.Width}x{view.Height} " +
                    $"min={photino.MinWidth}x{photino.MinHeight} max={photino.MaxWidth}x{photino.MaxHeight}");

                // Task 3.2 full-window mode: switching to a transparent title bar should make the
                // content view fill the whole frame, so inner height rises to the outer height.
                if (view is PhotinoWebView pv) pv.SetTitleBarStyle("transparent");
                var (tw, th) = view.GetInnerSize();
                Console.WriteLine($"[[CARBON_TITLEBAR]] inner_after={tw}x{th} outer={ow}x{oh}");

                // Task 3.8: the native drag call must be safe even with no active mouse press (it
                // no-ops rather than throwing). The actual window move needs a real drag to verify.
                view.StartDragging();
                Console.WriteLine("[[CARBON_DRAG]] start_dragging ok");

                // Task 3.3: drive chrome toggles and read the NSWindow state back, so the smoke proves
                // each actually changes the window rather than just returning.
                if (view is PhotinoWebView pw)
                {
                    view.SetDecorations(false);
                    var deco = pw.MacHasStyleBit("titled");
                    view.SetDecorations(true);
                    var decoBack = pw.MacHasStyleBit("titled");

                    view.SetClosable(false);
                    var closable = pw.MacHasStyleBit("closable");

                    view.SetContentProtected(true);
                    var protadd = pw.MacIsContentProtected();
                    view.SetContentProtected(false);

                    view.SetIgnoreCursorEvents(true);
                    var ignore = pw.MacIgnoresCursor();
                    view.SetIgnoreCursorEvents(false);
                    var ignoreBack = pw.MacIgnoresCursor();

                    // Exercise the rest for crash-safety; their effect isn't a simple readback here.
                    view.SetMinimizable(false);
                    view.SetMaximizable(false);
                    view.SetAlwaysOnBottom(true);
                    view.SetAlwaysOnBottom(false);
                    view.SetSkipTaskbar(true);

                    Console.WriteLine(
                        $"[[CARBON_CHROME]] deco_off={deco} deco_on={decoBack} closable_off={closable} " +
                        $"protected={protadd} ignore_on={ignore} ignore_off={ignoreBack}");
                }
                Console.Out.Flush();
            });
        });
    })
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

// AOT-safe payload typing for the JS -> C# smoke events above.
[JsonSerializable(typeof(string))]
internal partial class SmokeJsonContext : JsonSerializerContext;
