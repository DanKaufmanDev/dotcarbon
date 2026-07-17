using System.Runtime.InteropServices;
using Photino.NET;

namespace DotCarbon.Host.Desktop;

/// <summary>
/// Show / hide / focus / attention for a desktop window (Task 3.1).
///
/// Photino exposes none of these, so each is native. Windows can use Photino's <c>WindowHandle</c>
/// (its HWND — a Windows-only property). macOS and Linux get no handle from Photino, so the window is
/// resolved the same way the tray and menu backends resolve theirs: match the toplevel by title,
/// once, and cache it. Title collisions are possible but unlikely, and this is the only handle
/// Photino leaves us.
///
/// macOS AppKit is main-thread-only; these are invoked from bridge command handlers which, on the
/// desktop host, run on the UI thread, so no extra marshalling is added here.
/// </summary>
internal static unsafe class NativeWindowControls
{
    public static void Show(PhotinoWebView view)
    {
        if (OperatingSystem.IsWindows()) ShowWindow(Hwnd(view), SW_SHOW);
        else if (OperatingSystem.IsMacOS()) MacShow(MacWindow(view));
        else if (OperatingSystem.IsLinux()) LinuxShow(GtkWindow(view));
    }

    public static void Hide(PhotinoWebView view)
    {
        if (OperatingSystem.IsWindows()) ShowWindow(Hwnd(view), SW_HIDE);
        else if (OperatingSystem.IsMacOS()) MacOrderOut(MacWindow(view));
        else if (OperatingSystem.IsLinux()) GtkWidgetHide(GtkWindow(view));
    }

    public static void SetFocus(PhotinoWebView view)
    {
        if (OperatingSystem.IsWindows()) SetForegroundWindow(Hwnd(view));
        else if (OperatingSystem.IsMacOS()) MacFocus(MacWindow(view));
        else if (OperatingSystem.IsLinux()) gtk_window_present(GtkWindow(view));
    }

    public static bool IsVisible(PhotinoWebView view)
    {
        if (OperatingSystem.IsWindows()) return IsWindowVisible(Hwnd(view));
        if (OperatingSystem.IsMacOS()) return MacBool(MacWindow(view), "isVisible");
        if (OperatingSystem.IsLinux()) return gtk_widget_get_visible(GtkWindow(view));
        return true;
    }

    public static bool IsFocused(PhotinoWebView view)
    {
        if (OperatingSystem.IsWindows()) return Hwnd(view) == GetForegroundWindow();
        if (OperatingSystem.IsMacOS()) return MacBool(MacWindow(view), "isKeyWindow");
        if (OperatingSystem.IsLinux()) return gtk_window_is_active(GtkWindow(view));
        return false;
    }

    public static void RequestUserAttention(PhotinoWebView view)
    {
        if (OperatingSystem.IsWindows()) FlashTaskbar(Hwnd(view));
        else if (OperatingSystem.IsMacOS()) MacRequestAttention();
        else if (OperatingSystem.IsLinux()) gtk_window_set_urgency_hint(GtkWindow(view), true);
    }

    /// <summary>
    /// Begin an OS window-move drag (Task 3.8). Invoked from a mousedown on a drag region, so the
    /// press that started it is still the current event — each platform hands that press to its own
    /// move loop.
    /// </summary>
    public static void StartDragging(PhotinoWebView view)
    {
        if (OperatingSystem.IsWindows()) WinStartDrag(Hwnd(view));
        else if (OperatingSystem.IsMacOS()) MacStartDrag(MacWindow(view));
        else if (OperatingSystem.IsLinux()) LinuxStartDrag(GtkWindow(view));
    }

    // --- geometry: inner (content) vs outer (frame) (Task 3.2) -------------------------------
    // Photino exposes one size/position; the inner/outer split is native. Outer position comes from
    // Photino (top-left screen coords on every OS), which sidesteps the macOS bottom-left flip.

    public static (int, int) InnerSize(PhotinoWebView view)
    {
        if (OperatingSystem.IsWindows()) { GetClientRect(Hwnd(view), out var r); return (r.Right - r.Left, r.Bottom - r.Top); }
        if (OperatingSystem.IsMacOS()) { var s = MacContentSize(MacWindow(view)); return ((int)s.Width, (int)s.Height); }
        if (OperatingSystem.IsLinux()) { gtk_window_get_size(GtkWindow(view), out var w, out var h); return (w, h); }
        return (view.Width, view.Height);
    }

    public static (int, int) OuterSize(PhotinoWebView view)
    {
        if (OperatingSystem.IsWindows()) { GetWindowRect(Hwnd(view), out var r); return (r.Right - r.Left, r.Bottom - r.Top); }
        if (OperatingSystem.IsMacOS()) { var s = MacFrameSize(MacWindow(view)); return ((int)s.Width, (int)s.Height); }
        // GTK has no reliable cross-WM frame size; the content size is the honest answer (and exact
        // under a decorationless WM). Windows/macOS report the true frame.
        if (OperatingSystem.IsLinux()) { gtk_window_get_size(GtkWindow(view), out var w, out var h); return (w, h); }
        return (view.Width, view.Height);
    }

    public static (int, int) OuterPosition(PhotinoWebView view) => (view.X, view.Y);

    /// <summary>
    /// Apply the title-bar style (config `window.titleBarStyle`). "transparent" turns the window into
    /// a full-window app: the content view fills the whole frame, the traffic lights float over it,
    /// and the title is hidden. macOS only — Windows and Linux keep a normal title bar until custom
    /// frame work lands. "visible" (or anything else) is the default and does nothing.
    /// </summary>
    /// <returns>True if a non-default style was applied (so the caller can re-assert the size).</returns>
    public static bool SetTitleBarStyle(PhotinoWebView view, string? style)
    {
        if (!string.Equals(style, "transparent", StringComparison.OrdinalIgnoreCase)) return false;
        if (OperatingSystem.IsMacOS()) { MacFullSizeContent(MacWindow(view)); return true; }
        // Windows and Linux keep a normal title bar for now.
        return false;
    }

    private static void MacFullSizeContent(IntPtr window)
    {
        if (window == IntPtr.Zero) return;
        const long NSWindowStyleMaskFullSizeContentView = 1 << 15;
        const long NSWindowTitleVisibilityHidden = 1;

        var mask = (long)SendLong(window, Sel("styleMask"));
        SendSetLong(window, Sel("setStyleMask:"), (nint)(mask | NSWindowStyleMaskFullSizeContentView));
        SendBool(window, Sel("setTitlebarAppearsTransparent:"), true);
        SendSetLong(window, Sel("setTitleVisibility:"), (nint)NSWindowTitleVisibilityHidden);
    }

    public static (int, int) InnerPosition(PhotinoWebView view)
    {
        if (OperatingSystem.IsWindows())
        {
            var origin = new POINT { X = 0, Y = 0 };
            ClientToScreen(Hwnd(view), ref origin);
            return (origin.X, origin.Y);
        }
        // The content sits below the title bar; with no side borders on macOS/GTK the x is the frame x
        // and the y is offset by the decoration height. Derived from the sizes so no coordinate flip
        // (macOS frames are bottom-left origin) is needed.
        var (_, innerH) = InnerSize(view);
        var (_, outerH) = OuterSize(view);
        return (view.X, view.Y + (outerH - innerH));
    }

    // --- chrome & behavior (Task 3.3) --------------------------------------------------------
    // Photino exposes almost none of these at runtime, so each is native. macOS drives most off the
    // window's style mask; the readback helpers below let the smoke confirm the bits actually change.

    // macOS NSWindowStyleMask bits.
    private const long NSTitled = 1 << 0;
    private const long NSClosable = 1 << 1;
    private const long NSMiniaturizable = 1 << 2;

    public static void SetDecorations(PhotinoWebView view, bool on)
    {
        if (OperatingSystem.IsWindows()) WinToggleStyle(Hwnd(view), WS_CAPTION | WS_THICKFRAME, on);
        else if (OperatingSystem.IsMacOS()) MacSetStyleBit(MacWindow(view), NSTitled, on);
        else if (OperatingSystem.IsLinux()) gtk_window_set_decorated(GtkWindow(view), on);
    }

    public static void SetClosable(PhotinoWebView view, bool on)
    {
        if (OperatingSystem.IsWindows()) WinToggleStyle(Hwnd(view), WS_SYSMENU, on);
        else if (OperatingSystem.IsMacOS()) MacSetStyleBit(MacWindow(view), NSClosable, on);
        else if (OperatingSystem.IsLinux()) gtk_window_set_deletable(GtkWindow(view), on);
    }

    public static void SetMinimizable(PhotinoWebView view, bool on)
    {
        if (OperatingSystem.IsWindows()) WinToggleStyle(Hwnd(view), WS_MINIMIZEBOX, on);
        else if (OperatingSystem.IsMacOS()) MacSetStyleBit(MacWindow(view), NSMiniaturizable, on);
        // Linux/GTK has no reliable per-button toggle.
    }

    public static void SetMaximizable(PhotinoWebView view, bool on)
    {
        if (OperatingSystem.IsWindows()) WinToggleStyle(Hwnd(view), WS_MAXIMIZEBOX, on);
        else if (OperatingSystem.IsMacOS()) MacSetZoomEnabled(MacWindow(view), on);
        // Linux/GTK has no reliable per-button toggle.
    }

    public static void SetAlwaysOnBottom(PhotinoWebView view, bool on)
    {
        if (OperatingSystem.IsWindows()) WinSetBottom(Hwnd(view), on);
        else if (OperatingSystem.IsMacOS()) SendSetLong(MacWindow(view), Sel("setLevel:"), (nint)(on ? -1 : 0));
        else if (OperatingSystem.IsLinux()) gtk_window_set_keep_below(GtkWindow(view), on);
    }

    public static void SetSkipTaskbar(PhotinoWebView view, bool on)
    {
        if (OperatingSystem.IsWindows()) WinSetToolWindow(Hwnd(view), on);
        else if (OperatingSystem.IsLinux()) gtk_window_set_skip_taskbar_hint(GtkWindow(view), on);
        // macOS has no per-window taskbar entry (the dock is per-app).
    }

    public static void SetContentProtected(PhotinoWebView view, bool on)
    {
        // macOS NSWindowSharingNone = 0, NSWindowSharingReadOnly = 1 (the default).
        if (OperatingSystem.IsWindows()) SetWindowDisplayAffinity(Hwnd(view), on ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
        else if (OperatingSystem.IsMacOS()) SendSetLong(MacWindow(view), Sel("setSharingType:"), (nint)(on ? 0 : 1));
        // Linux has no standard capture-protection API.
    }

    public static void SetIgnoreCursorEvents(PhotinoWebView view, bool on)
    {
        if (OperatingSystem.IsWindows()) WinSetClickThrough(Hwnd(view), on);
        else if (OperatingSystem.IsMacOS()) SendBool(MacWindow(view), Sel("setIgnoresMouseEvents:"), on);
        else if (OperatingSystem.IsLinux()) LinuxSetClickThrough(GtkWindow(view), on);
    }

    // --- taskbar progress + dock badge (Task 3.9) --------------------------------------------

    /// <summary>
    /// Set the taskbar progress bar. <paramref name="progress"/> is 0–100. Windows only (via
    /// ITaskbarList3); macOS has no taskbar progress, and Linux's is a desktop-specific D-Bus protocol
    /// — both are no-ops here.
    /// </summary>
    public static void SetProgressBar(PhotinoWebView view, string status, int progress)
    {
        if (OperatingSystem.IsWindows()) WinSetProgress(Hwnd(view), status, progress);
    }

    /// <summary>
    /// Set (or clear, with null) the app's badge. macOS shows it on the dock icon. Windows would need
    /// a generated overlay icon and Linux a Unity count — both deferred, so no-ops there.
    /// </summary>
    public static void SetBadge(string? label)
    {
        if (OperatingSystem.IsMacOS()) MacSetBadge(label);
    }

    /// <summary>macOS: read the dock badge back, for verification.</summary>
    public static string? MacGetBadge()
    {
        var tile = Send(Send(Cls("NSApplication"), Sel("sharedApplication")), Sel("dockTile"));
        var label = Send(tile, Sel("badgeLabel"));
        return label == IntPtr.Zero ? null
            : Marshal.PtrToStringUTF8(Send(label, Sel("UTF8String")));
    }

    private static void MacSetBadge(string? label)
    {
        var tile = Send(Send(Cls("NSApplication"), Sel("sharedApplication")), Sel("dockTile"));
        SendPtr(tile, Sel("setBadgeLabel:"), label is null ? IntPtr.Zero : NSString(label));
    }

    // --- theme (Task 3.6) --------------------------------------------------------------------

    public static string GetTheme(PhotinoWebView view)
    {
        if (OperatingSystem.IsWindows()) return WinAppsUseLightTheme() ? "light" : "dark";
        if (OperatingSystem.IsMacOS()) return MacGetTheme(MacWindow(view));
        if (OperatingSystem.IsLinux()) return LinuxPrefersDark() ? "dark" : "light";
        return "light";
    }

    /// <summary>Override the window's theme: "light", "dark", or "auto" (follow the OS).</summary>
    public static void SetTheme(PhotinoWebView view, string theme)
    {
        if (OperatingSystem.IsWindows()) WinSetDarkTitleBar(Hwnd(view), theme);
        else if (OperatingSystem.IsMacOS()) MacSetTheme(MacWindow(view), theme);
        else if (OperatingSystem.IsLinux()) LinuxSetPreferDark(theme);
    }

    private static string MacGetTheme(IntPtr window)
    {
        var appearance = window == IntPtr.Zero
            ? Send(Send(Cls("NSApplication"), Sel("sharedApplication")), Sel("effectiveAppearance"))
            : Send(window, Sel("effectiveAppearance"));
        if (appearance == IntPtr.Zero) return "light";
        var name = Marshal.PtrToStringUTF8(Send(Send(appearance, Sel("name")), Sel("UTF8String"))) ?? "";
        return name.Contains("Dark", StringComparison.Ordinal) ? "dark" : "light";
    }

    private static void MacSetTheme(IntPtr window, string theme)
    {
        if (window == IntPtr.Zero) return;
        if (string.Equals(theme, "auto", StringComparison.OrdinalIgnoreCase))
        {
            SendPtr(window, Sel("setAppearance:"), IntPtr.Zero); // nil = follow the system
            return;
        }
        var named = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase)
            ? "NSAppearanceNameDarkAqua" : "NSAppearanceNameAqua";
        var appearance = SendPtr(Cls("NSAppearance"), Sel("appearanceNamed:"), NSString(named));
        SendPtr(window, Sel("setAppearance:"), appearance);
    }

    private static IntPtr NSString(string value) =>
        SendStr(Cls("NSString"), Sel("stringWithUTF8String:"), value);

    // --- cursor (Task 3.4) -------------------------------------------------------------------
    // Position is relative to the window's top-left content, matching the JS API; the others act on
    // the shared cursor. Only position has a clean readback, so it is the verified one.

    public static void SetCursorPosition(PhotinoWebView view, int x, int y)
    {
        var (ox, oy) = OuterPosition(view);
        if (OperatingSystem.IsWindows()) SetCursorPos(ox + x, oy + y);
        else if (OperatingSystem.IsMacOS()) CGWarpMouseCursorPosition(new CGPoint { X = ox + x, Y = oy + y });
        else if (OperatingSystem.IsLinux()) LinuxWarpCursor(ox + x, oy + y);
    }

    public static void SetCursorVisible(PhotinoWebView view, bool visible)
    {
        if (OperatingSystem.IsWindows()) ShowCursor(visible);
        else if (OperatingSystem.IsMacOS()) Send(Cls("NSCursor"), Sel(visible ? "unhide" : "hide"));
        else if (OperatingSystem.IsLinux()) LinuxSetCursorVisible(GtkWindow(view), visible);
    }

    public static void SetCursorGrab(PhotinoWebView view, bool grab)
    {
        // The closest cross-platform notion of "grab" is confining the cursor to the window.
        if (OperatingSystem.IsWindows()) WinClipCursor(view, grab);
        // macOS: decouple the cursor from mouse deltas (0 = locked/grabbed, 1 = normal).
        else if (OperatingSystem.IsMacOS()) CGAssociateMouseAndMouseCursorPosition(grab ? 0 : 1);
        // Linux grab is a deprecated per-seat operation; left out until there is a way to verify it.
    }

    public static void SetCursorIcon(PhotinoWebView view, string icon)
    {
        if (OperatingSystem.IsWindows()) WinSetCursor(icon);
        else if (OperatingSystem.IsMacOS()) MacSetCursor(icon);
        else if (OperatingSystem.IsLinux()) LinuxSetCursor(GtkWindow(view), icon);
    }

    /// <summary>macOS: the cursor's current screen position (top-left origin), for verification.</summary>
    public static (int, int) MacGlobalCursor()
    {
        var mouseEvent = CGEventCreate(IntPtr.Zero);
        var point = CGEventGetLocation(mouseEvent);
        if (mouseEvent != IntPtr.Zero) CFRelease(mouseEvent);
        return ((int)point.X, (int)point.Y);
    }

    private static void MacSetCursor(string icon)
    {
        var selector = icon.ToLowerInvariant() switch
        {
            "pointer" or "hand" => "pointingHandCursor",
            "text" => "IBeamCursor",
            "crosshair" => "crosshairCursor",
            "grab" => "openHandCursor",
            "grabbing" or "move" => "closedHandCursor",
            "notallowed" or "not-allowed" => "operationNotAllowedCursor",
            "ew-resize" or "col-resize" => "resizeLeftRightCursor",
            "ns-resize" or "row-resize" => "resizeUpDownCursor",
            _ => "arrowCursor",
        };
        var cursor = Send(Cls("NSCursor"), Sel(selector));
        if (cursor != IntPtr.Zero) Send(cursor, Sel("set"));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint { public double X, Y; }

    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [DllImport(CoreGraphics)] private static extern int CGWarpMouseCursorPosition(CGPoint point);
    [DllImport(CoreGraphics)] private static extern int CGAssociateMouseAndMouseCursorPosition(int connected);
    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(CoreGraphics)] private static extern CGPoint CGEventGetLocation(IntPtr mouseEvent);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr cf);

    // --- macOS chrome readbacks (verification) -----------------------------------------------

    /// <summary>Whether a style-mask bit is set — lets the smoke confirm a toggle actually took.</summary>
    public static bool MacHasStyleBit(PhotinoWebView view, string which)
    {
        var window = MacWindow(view);
        if (window == IntPtr.Zero) return false;
        var mask = (long)SendLong(window, Sel("styleMask"));
        var bit = which switch
        {
            "titled" => NSTitled,
            "closable" => NSClosable,
            "miniaturizable" => NSMiniaturizable,
            _ => 0L,
        };
        return (mask & bit) != 0;
    }

    public static bool MacIsContentProtected(PhotinoWebView view)
    {
        var window = MacWindow(view);
        return window != IntPtr.Zero && (long)SendLong(window, Sel("sharingType")) == 0;
    }

    public static bool MacIgnoresCursor(PhotinoWebView view) =>
        MacBool(MacWindow(view), "ignoresMouseEvents");

    private static void MacSetStyleBit(IntPtr window, long bit, bool on)
    {
        if (window == IntPtr.Zero) return;
        var mask = (long)SendLong(window, Sel("styleMask"));
        SendSetLong(window, Sel("setStyleMask:"), (nint)(on ? mask | bit : mask & ~bit));
    }

    private static void MacSetZoomEnabled(IntPtr window, bool on)
    {
        if (window == IntPtr.Zero) return;
        const long NSWindowZoomButton = 2;
        var button = SendLongArg(window, Sel("standardWindowButton:"), NSWindowZoomButton);
        if (button != IntPtr.Zero) SendBool(button, Sel("setEnabled:"), on);
    }

    // --- Windows -----------------------------------------------------------------------------

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const uint FLASHW_ALL = 0x3;
    private const uint FLASHW_TIMERNOFG = 0xC; // flash until the window comes to the foreground
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 2; // "as if the title bar was grabbed"

    private static IntPtr Hwnd(PhotinoWebView view) => view.Window.WindowHandle;

    private static void WinStartDrag(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        // Drop the webview's mouse capture, then tell the window it was grabbed by its title bar —
        // Windows runs the move loop from there.
        ReleaseCapture();
        SendMessageW(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    private static void FlashTaskbar(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
            uCount = uint.MaxValue,
            dwTimeout = 0,
        };
        FlashWindowEx(ref info);
    }

    // Window styles (Task 3.3).
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const long WS_CAPTION = 0x00C00000;
    private const long WS_THICKFRAME = 0x00040000;
    private const long WS_SYSMENU = 0x00080000;
    private const long WS_MINIMIZEBOX = 0x00020000;
    private const long WS_MAXIMIZEBOX = 0x00010000;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const long WS_EX_LAYERED = 0x00080000;
    private const uint WDA_NONE = 0x00;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    private static readonly IntPtr HWND_BOTTOM = new(1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private static void WinToggleStyle(IntPtr hwnd, long style, bool on) =>
        WinToggle(hwnd, GWL_STYLE, style, on);

    private static void WinSetToolWindow(IntPtr hwnd, bool on) =>
        WinToggle(hwnd, GWL_EXSTYLE, WS_EX_TOOLWINDOW, on);

    private static void WinSetClickThrough(IntPtr hwnd, bool on) =>
        WinToggle(hwnd, GWL_EXSTYLE, WS_EX_TRANSPARENT | WS_EX_LAYERED, on);

    private static void WinToggle(IntPtr hwnd, int index, long flag, bool on)
    {
        if (hwnd == IntPtr.Zero) return;
        var current = (long)GetWindowLongPtrW(hwnd, index);
        var updated = on ? current | flag : current & ~flag;
        SetWindowLongPtrW(hwnd, index, (IntPtr)updated);
        // A GWL_STYLE change only takes visual effect after a frame recalculation.
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    private static void WinSetBottom(IntPtr hwnd, bool on)
    {
        if (hwnd == IntPtr.Zero) return;
        // Sending it to the bottom once is enough to drop it under the stack; there is no persistent
        // "keep below" on Windows, matching the platform's own behavior.
        if (on) SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
    }

    private static void WinClipCursor(PhotinoWebView view, bool grab)
    {
        if (!grab) { ClipCursor(IntPtr.Zero); return; }
        var hwnd = Hwnd(view);
        if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var rect)) ClipCursor(ref rect);
    }

    // Theme (Task 3.6).
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private static bool WinAppsUseLightTheme()
    {
        // HKCU\...\Themes\Personalize\AppsUseLightTheme (1 = light, 0 = dark). Default light if absent.
        var data = 1;
        var size = sizeof(int);
        const int RRF_RT_REG_DWORD = 0x00000010;
        var result = RegGetValueW(HKEY_CURRENT_USER,
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", RRF_RT_REG_DWORD, out _, ref data, ref size);
        return result != 0 || data != 0; // missing key → treat as light
    }

    private static void WinSetDarkTitleBar(IntPtr hwnd, string theme)
    {
        if (hwnd == IntPtr.Zero) return;
        var dark = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(theme, "auto", StringComparison.OrdinalIgnoreCase) && !WinAppsUseLightTheme());
        var flag = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref flag, sizeof(int));
    }

    // ITaskbarList3 progress (Task 3.9), called through the raw vtable to stay AOT/trim-clean (no
    // ComImport). Resolved once and cached.
    private static IntPtr _taskbarList;
    private static bool _taskbarTried;

    // TBPFLAG
    private const int TBPF_NOPROGRESS = 0;
    private const int TBPF_INDETERMINATE = 1;
    private const int TBPF_NORMAL = 2;
    private const int TBPF_ERROR = 4;
    private const int TBPF_PAUSED = 8;

    private static IntPtr TaskbarList()
    {
        if (_taskbarTried) return _taskbarList;
        _taskbarTried = true;
        var clsid = new Guid("56FDF344-FD6D-11d0-958A-006097C9A090"); // CLSID_TaskbarList
        var iid = new Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf");   // IID_ITaskbarList3
        if (CoCreateInstance(ref clsid, IntPtr.Zero, 1 /*CLSCTX_INPROC_SERVER*/, ref iid, out var tb) != 0
            || tb == IntPtr.Zero)
            return IntPtr.Zero;

        var vtbl = *(IntPtr**)tb;
        var hrInit = (delegate* unmanaged<IntPtr, int>)vtbl[3]; // ITaskbarList::HrInit
        hrInit(tb);
        _taskbarList = tb;
        return tb;
    }

    private static void WinSetProgress(IntPtr hwnd, string status, int progress)
    {
        var tb = TaskbarList();
        if (tb == IntPtr.Zero || hwnd == IntPtr.Zero) return;

        var flag = status.ToLowerInvariant() switch
        {
            "none" => TBPF_NOPROGRESS,
            "indeterminate" => TBPF_INDETERMINATE,
            "paused" => TBPF_PAUSED,
            "error" => TBPF_ERROR,
            _ => TBPF_NORMAL,
        };

        var vtbl = *(IntPtr**)tb;
        // Vtable order: 9 = SetProgressValue(hwnd, completed, total), 10 = SetProgressState(hwnd, flags).
        var setState = (delegate* unmanaged<IntPtr, IntPtr, int, int>)vtbl[10];
        setState(tb, hwnd, flag);
        if (flag is TBPF_NORMAL or TBPF_PAUSED or TBPF_ERROR)
        {
            var value = (ulong)Math.Clamp(progress, 0, 100);
            var setValue = (delegate* unmanaged<IntPtr, IntPtr, ulong, ulong, int>)vtbl[9];
            setValue(tb, hwnd, value, 100);
        }
    }

    private static void WinSetCursor(string icon)
    {
        // IDC_* standard cursors.
        var id = icon.ToLowerInvariant() switch
        {
            "pointer" or "hand" => 32649,       // IDC_HAND
            "text" => 32513,                     // IDC_IBEAM
            "crosshair" => 32515,                // IDC_CROSS
            "wait" => 32514,                     // IDC_WAIT
            "help" => 32651,                     // IDC_HELP
            "move" => 32646,                     // IDC_SIZEALL
            "notallowed" or "not-allowed" => 32648, // IDC_NO
            "ew-resize" or "col-resize" => 32644,   // IDC_SIZEWE
            "ns-resize" or "row-resize" => 32645,   // IDC_SIZENS
            _ => 32512,                          // IDC_ARROW
        };
        var cursor = LoadCursorW(IntPtr.Zero, id);
        if (cursor != IntPtr.Zero) SetCursor(cursor);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FLASHWINFO info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessageW(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool show);
    [DllImport("user32.dll")] private static extern bool ClipCursor(ref RECT rect);
    [DllImport("user32.dll")] private static extern bool ClipCursor(IntPtr rect);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursorW(IntPtr hInstance, int cursorId);
    [DllImport("user32.dll")] private static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, int context, ref Guid iid, out IntPtr instance);
    private static readonly IntPtr HKEY_CURRENT_USER = unchecked((IntPtr)(int)0x80000001);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegGetValueW(IntPtr hkey, string subKey, string value, int flags, out int type, ref int data, ref int dataSize);

    // --- macOS -------------------------------------------------------------------------------

    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private const long NSCriticalRequest = 0;

    /// <summary>The NSWindow whose title matches, cached on the view so a later retitle can't lose it.</summary>
    private static IntPtr MacWindow(PhotinoWebView view)
    {
        if (view.NativeWindow != IntPtr.Zero) return view.NativeWindow;

        var app = Send(Cls("NSApplication"), Sel("sharedApplication"));
        var windows = Send(app, Sel("windows"));
        var count = (long)SendLong(windows, Sel("count"));
        for (var i = 0L; i < count; i++)
        {
            var window = SendIdx(windows, Sel("objectAtIndex:"), i);
            var title = Marshal.PtrToStringUTF8(Send(Send(window, Sel("title")), Sel("UTF8String"))) ?? "";
            if (title == view.Title)
            {
                view.NativeWindow = window;
                return window;
            }
        }
        return IntPtr.Zero;
    }

    private static void MacShow(IntPtr window)
    {
        if (window != IntPtr.Zero) SendPtr(window, Sel("makeKeyAndOrderFront:"), IntPtr.Zero);
    }

    private static void MacOrderOut(IntPtr window)
    {
        if (window != IntPtr.Zero) SendPtr(window, Sel("orderOut:"), IntPtr.Zero);
    }

    private static void MacFocus(IntPtr window)
    {
        var app = Send(Cls("NSApplication"), Sel("sharedApplication"));
        SendBool(app, Sel("activateIgnoringOtherApps:"), true);
        if (window != IntPtr.Zero) SendPtr(window, Sel("makeKeyAndOrderFront:"), IntPtr.Zero);
    }

    private static void MacRequestAttention()
    {
        var app = Send(Cls("NSApplication"), Sel("sharedApplication"));
        SendLongArg(app, Sel("requestUserAttention:"), NSCriticalRequest);
    }

    private static bool MacBool(IntPtr window, string selector) =>
        window != IntPtr.Zero && SendReturnBool(window, Sel(selector));

    private static void MacStartDrag(IntPtr window)
    {
        if (window == IntPtr.Zero) return;
        var app = Send(Cls("NSApplication"), Sel("sharedApplication"));
        var mouseEvent = Send(app, Sel("currentEvent"));
        // The current event is the mousedown that triggered the drag-region handler; AppKit runs the
        // move loop from it. Nil (e.g. no active mouse press) means there is nothing to drag from.
        if (mouseEvent != IntPtr.Zero) SendPtr(window, Sel("performWindowDragWithEvent:"), mouseEvent);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGSize { public double Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect { public double X, Y, Width, Height; }

    private static CGSize MacFrameSize(IntPtr window) =>
        window == IntPtr.Zero ? default : MacRect(window, "frame").Size();

    private static CGSize MacContentSize(IntPtr window)
    {
        if (window == IntPtr.Zero) return default;
        var content = Send(window, Sel("contentView"));
        return content == IntPtr.Zero ? default : MacRect(content, "frame").Size();
    }

    private static CGSize Size(this CGRect rect) => new() { Width = rect.Width, Height = rect.Height };

    /// <summary>
    /// Read an NSRect-returning selector. NSRect is 32 bytes: x86_64 classes it MEMORY and returns it
    /// through a hidden pointer (objc_msgSend_stret), while arm64 returns it in registers and has no
    /// _stret symbol. Calling the wrong entry point yields garbage rather than a crash — pick by arch.
    /// </summary>
    private static CGRect MacRect(IntPtr receiver, string selector)
    {
        var sel = Sel(selector);
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64) return SendRect(receiver, sel);
        SendRectStret(out var rect, receiver, sel);
        return rect;
    }

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern CGRect SendRect(IntPtr receiver, IntPtr sel);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend_stret")] private static extern void SendRectStret(out CGRect result, IntPtr receiver, IntPtr sel);

    [DllImport(LibObjC)] private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(LibObjC)] private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr sel);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr receiver, IntPtr sel, IntPtr arg);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendIdx(IntPtr receiver, IntPtr sel, long arg);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendStr(IntPtr receiver, IntPtr sel, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendLongArg(IntPtr receiver, IntPtr sel, long arg);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern nint SendLong(IntPtr receiver, IntPtr sel);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void SendBool(IntPtr receiver, IntPtr sel, [MarshalAs(UnmanagedType.I1)] bool arg);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void SendSetLong(IntPtr receiver, IntPtr sel, nint arg);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SendReturnBool(IntPtr receiver, IntPtr sel);

    private static IntPtr Cls(string name) => objc_getClass(name);
    private static IntPtr Sel(string name) => sel_registerName(name);

    // --- Linux -------------------------------------------------------------------------------

    private const string Gtk = "libgtk-3.so.0";
    private const string GLib = "libglib-2.0.so.0";
    private const string Gdk = "libgdk-3.so.0";
    private const string GObject = "libgobject-2.0.so.0";

    /// <summary>The GtkWindow whose title matches, cached on the view (same approach as LinuxMenu).</summary>
    private static IntPtr GtkWindow(PhotinoWebView view)
    {
        if (view.NativeWindow != IntPtr.Zero) return view.NativeWindow;

        var list = gtk_window_list_toplevels();
        try
        {
            for (var node = list; node != IntPtr.Zero; node = Marshal.ReadIntPtr(node, IntPtr.Size))
            {
                var window = Marshal.ReadIntPtr(node);
                if (window == IntPtr.Zero) continue;
                var title = Marshal.PtrToStringUTF8(gtk_window_get_title(window));
                if (string.Equals(title, view.Title, StringComparison.Ordinal))
                {
                    view.NativeWindow = window;
                    return window;
                }
            }
        }
        finally
        {
            if (list != IntPtr.Zero) g_list_free(list);
        }
        return IntPtr.Zero;
    }

    private static void LinuxShow(IntPtr window)
    {
        if (window == IntPtr.Zero) return;
        gtk_widget_show(window);
        gtk_window_present(window);
    }

    /// <summary>
    /// Toggle click-through (Task 3.3). An empty input-shape region makes GTK pass every pointer event
    /// under the window; a null region restores normal input.
    /// </summary>
    private static void LinuxSetClickThrough(IntPtr window, bool on)
    {
        if (window == IntPtr.Zero) return;
        var gdkWindow = gtk_widget_get_window(window);
        if (gdkWindow == IntPtr.Zero) return;
        if (on)
        {
            var empty = cairo_region_create();
            gdk_window_input_shape_combine_region(gdkWindow, empty, 0, 0);
            cairo_region_destroy(empty);
        }
        else
        {
            gdk_window_input_shape_combine_region(gdkWindow, IntPtr.Zero, 0, 0);
        }
    }

    // Theme (Task 3.6): GtkSettings' gtk-application-prefer-dark-theme.
    private static bool LinuxPrefersDark()
    {
        var settings = gtk_settings_get_default();
        if (settings == IntPtr.Zero) return false;
        g_object_get_bool(settings, "gtk-application-prefer-dark-theme", out var dark, IntPtr.Zero);
        return dark;
    }

    private static void LinuxSetPreferDark(string theme)
    {
        var settings = gtk_settings_get_default();
        if (settings == IntPtr.Zero) return;
        // "auto" leaves the setting to the desktop; only an explicit choice overrides it.
        if (string.Equals(theme, "auto", StringComparison.OrdinalIgnoreCase)) return;
        var dark = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase);
        g_object_set_bool(settings, "gtk-application-prefer-dark-theme", dark, IntPtr.Zero);
    }

    private static void LinuxWarpCursor(int x, int y)
    {
        var display = gdk_display_get_default();
        if (display == IntPtr.Zero) return;
        var pointer = gdk_seat_get_pointer(gdk_display_get_default_seat(display));
        var screen = gdk_display_get_default_screen(display);
        gdk_device_warp(pointer, screen, x, y);
    }

    private static void LinuxSetCursorVisible(IntPtr window, bool visible)
    {
        var gdkWindow = window == IntPtr.Zero ? IntPtr.Zero : gtk_widget_get_window(window);
        if (gdkWindow == IntPtr.Zero) return;
        var display = gdk_display_get_default();
        const int GdkBlankCursor = 25;
        var cursor = visible ? IntPtr.Zero : gdk_cursor_new_for_display(display, GdkBlankCursor);
        gdk_window_set_cursor(gdkWindow, cursor);
    }

    private static void LinuxSetCursor(IntPtr window, string icon)
    {
        var gdkWindow = window == IntPtr.Zero ? IntPtr.Zero : gtk_widget_get_window(window);
        if (gdkWindow == IntPtr.Zero) return;
        var name = icon.ToLowerInvariant() switch
        {
            "pointer" or "hand" => "pointer",
            "text" => "text",
            "crosshair" => "crosshair",
            "wait" => "wait",
            "help" => "help",
            "move" => "move",
            "notallowed" or "not-allowed" => "not-allowed",
            "grab" => "grab",
            "grabbing" => "grabbing",
            "ew-resize" or "col-resize" => "ew-resize",
            "ns-resize" or "row-resize" => "ns-resize",
            _ => "default",
        };
        var cursor = gdk_cursor_new_from_name(gdk_display_get_default(), name);
        gdk_window_set_cursor(gdkWindow, cursor);
    }

    private static void LinuxStartDrag(IntPtr window)
    {
        if (window == IntPtr.Zero) return;
        // begin_move_drag wants the pointer press that started it. We are called from the bridge, not
        // a GTK signal, so read the pointer's current root position from the default seat.
        var display = gdk_display_get_default();
        if (display == IntPtr.Zero) return;
        var seat = gdk_display_get_default_seat(display);
        var pointer = gdk_seat_get_pointer(seat);
        gdk_device_get_position(pointer, out _, out var x, out var y);
        const int GdkCurrentTime = 0;
        gtk_window_begin_move_drag(window, button: 1, x, y, GdkCurrentTime);
    }

    private static void GtkWidgetHide(IntPtr window)
    {
        if (window != IntPtr.Zero) gtk_widget_hide(window);
    }

    [DllImport(Gtk)] private static extern IntPtr gtk_window_list_toplevels();
    [DllImport(Gtk)] private static extern IntPtr gtk_window_get_title(IntPtr window);
    [DllImport(Gtk)] private static extern void gtk_widget_show(IntPtr widget);
    [DllImport(Gtk)] private static extern void gtk_widget_hide(IntPtr widget);
    [DllImport(Gtk)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool gtk_widget_get_visible(IntPtr widget);
    [DllImport(Gtk)] private static extern void gtk_window_present(IntPtr window);
    [DllImport(Gtk)] private static extern void gtk_window_get_size(IntPtr window, out int width, out int height);
    [DllImport(Gtk)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool gtk_window_is_active(IntPtr window);
    [DllImport(Gtk)] private static extern void gtk_window_set_urgency_hint(IntPtr window, [MarshalAs(UnmanagedType.I1)] bool urgent);
    [DllImport(Gtk)] private static extern void gtk_window_begin_move_drag(IntPtr window, int button, int rootX, int rootY, int timestamp);
    [DllImport(Gtk)] private static extern void gtk_window_set_decorated(IntPtr window, [MarshalAs(UnmanagedType.I1)] bool setting);
    [DllImport(Gtk)] private static extern void gtk_window_set_deletable(IntPtr window, [MarshalAs(UnmanagedType.I1)] bool setting);
    [DllImport(Gtk)] private static extern void gtk_window_set_keep_below(IntPtr window, [MarshalAs(UnmanagedType.I1)] bool setting);
    [DllImport(Gtk)] private static extern void gtk_window_set_skip_taskbar_hint(IntPtr window, [MarshalAs(UnmanagedType.I1)] bool setting);
    [DllImport(Gtk)] private static extern IntPtr gtk_widget_get_window(IntPtr widget);
    [DllImport(Gtk)] private static extern IntPtr gtk_settings_get_default();
    [DllImport(GObject, EntryPoint = "g_object_get")] private static extern void g_object_get_bool(IntPtr obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string prop, [MarshalAs(UnmanagedType.I1)] out bool value, IntPtr terminator);
    [DllImport(GObject, EntryPoint = "g_object_set")] private static extern void g_object_set_bool(IntPtr obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string prop, [MarshalAs(UnmanagedType.I1)] bool value, IntPtr terminator);
    [DllImport(Gdk)] private static extern void gdk_window_input_shape_combine_region(IntPtr window, IntPtr shape, int offsetX, int offsetY);
    [DllImport("libcairo.so.2")] private static extern IntPtr cairo_region_create();
    [DllImport("libcairo.so.2")] private static extern void cairo_region_destroy(IntPtr region);
    [DllImport(Gdk)] private static extern IntPtr gdk_display_get_default();
    [DllImport(Gdk)] private static extern IntPtr gdk_display_get_default_seat(IntPtr display);
    [DllImport(Gdk)] private static extern IntPtr gdk_display_get_default_screen(IntPtr display);
    [DllImport(Gdk)] private static extern IntPtr gdk_seat_get_pointer(IntPtr seat);
    [DllImport(Gdk)] private static extern void gdk_device_get_position(IntPtr device, out IntPtr screen, out int x, out int y);
    [DllImport(Gdk)] private static extern void gdk_device_warp(IntPtr device, IntPtr screen, int x, int y);
    [DllImport(Gdk)] private static extern void gdk_window_set_cursor(IntPtr window, IntPtr cursor);
    [DllImport(Gdk)] private static extern IntPtr gdk_cursor_new_for_display(IntPtr display, int cursorType);
    [DllImport(Gdk)] private static extern IntPtr gdk_cursor_new_from_name(IntPtr display, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(GLib)] private static extern void g_list_free(IntPtr list);
}
