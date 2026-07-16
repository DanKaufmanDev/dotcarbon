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
    [DllImport(Gdk)] private static extern IntPtr gdk_display_get_default();
    [DllImport(Gdk)] private static extern IntPtr gdk_display_get_default_seat(IntPtr display);
    [DllImport(Gdk)] private static extern IntPtr gdk_seat_get_pointer(IntPtr seat);
    [DllImport(Gdk)] private static extern void gdk_device_get_position(IntPtr device, out IntPtr screen, out int x, out int y);
    [DllImport(GLib)] private static extern void g_list_free(IntPtr list);
}
