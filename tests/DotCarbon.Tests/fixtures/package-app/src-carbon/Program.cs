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

                // Task 3.3: the toggles are cross-platform safe (they branch internally); exercise
                // them everywhere for crash-safety.
                view.SetDecorations(false);
                view.SetDecorations(true);
                view.SetClosable(false);
                view.SetContentProtected(true);
                view.SetContentProtected(false);
                view.SetIgnoreCursorEvents(true);
                view.SetIgnoreCursorEvents(false);
                view.SetMinimizable(false);
                view.SetMaximizable(false);
                view.SetAlwaysOnBottom(true);
                view.SetAlwaysOnBottom(false);
                view.SetSkipTaskbar(true);

                // The state readbacks are macOS-only (they call libobjc, which does not exist
                // elsewhere), so only re-drive with readback there; other OSes just prove no crash.
                if (view is PhotinoWebView pw && OperatingSystem.IsMacOS())
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
                    Console.WriteLine(
                        $"[[CARBON_CHROME]] deco_off={deco} deco_on={decoBack} closable_off={closable} " +
                        $"protected={protadd} ignore_on={ignore} ignore_off={ignoreBack}");
                }
                else
                {
                    Console.WriteLine("[[CARBON_CHROME]] toggles ran (no readback off macOS)");
                }

                // Task 3.4: cursor. Exercise the setters everywhere (crash-safety), leaving the cursor
                // visible and ungrabbed. Verify position by warping and reading it back — macOS only,
                // since the readback goes through CoreGraphics.
                view.SetCursorIcon("pointer");
                view.SetCursorIcon("default");
                view.SetCursorVisible(false);
                view.SetCursorVisible(true);
                view.SetCursorGrab(true);
                view.SetCursorGrab(false);
                if (view is PhotinoWebView cpw && OperatingSystem.IsMacOS())
                {
                    var (cox, coy) = view.GetOuterPosition();
                    view.SetCursorPosition(200, 150);
                    var (cx, cy) = cpw.MacGlobalCursor();
                    Console.WriteLine(
                        $"[[CARBON_CURSOR]] target={cox + 200},{coy + 150} actual={cx},{cy} " +
                        $"dx={Math.Abs(cx - (cox + 200))} dy={Math.Abs(cy - (coy + 150))}");
                }
                else
                {
                    Console.WriteLine("[[CARBON_CURSOR]] cursor setters ran (no readback off macOS)");
                }

                // Task 3.5: monitors. Pure readback from Photino, so this runs on every OS.
                var monitors = view.GetMonitors();
                var primary = view.GetPrimaryMonitor();
                var current = view.GetCurrentMonitor();
                var scale = view.GetScaleFactor();
                Console.WriteLine(
                    $"[[CARBON_MON]] count={monitors.Count} " +
                    $"primary={(primary is null ? "null" : $"{primary.Width}x{primary.Height}@{primary.ScaleFactor}")} " +
                    $"current={(current is null ? "null" : $"{current.Width}x{current.Height}")} scale={scale}");

                // Task 3.6: theme. Set an override and read the effective theme back. On macOS the
                // window's effectiveAppearance reflects the override; the readback runs everywhere
                // (it branches internally), so this is exercised on all OSes.
                var initialTheme = view.GetTheme();
                view.SetTheme("dark");
                var afterDark = view.GetTheme();
                view.SetTheme("light");
                var afterLight = view.GetTheme();
                view.SetTheme("auto"); // leave it following the system
                Console.WriteLine(
                    $"[[CARBON_THEME]] initial={initialTheme} after_dark={afterDark} after_light={afterLight}");

                // Restore a normal, closable window. On macOS a window with the closable style bit
                // removed cannot be closed programmatically (performClose: is a no-op), which would
                // leave the smoke unable to exit — so the toggles must not leave it in that state.
                view.SetClosable(true);
                view.SetDecorations(true);
                view.SetMinimizable(true);
                view.SetMaximizable(true);
                view.SetSkipTaskbar(false);
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
