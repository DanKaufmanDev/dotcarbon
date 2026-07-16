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

    // --- Windows -----------------------------------------------------------------------------

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const uint FLASHW_ALL = 0x3;
    private const uint FLASHW_TIMERNOFG = 0xC; // flash until the window comes to the foreground

    private static IntPtr Hwnd(PhotinoWebView view) => view.Window.WindowHandle;

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

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FLASHWINFO info);

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

    [DllImport(LibObjC)] private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(LibObjC)] private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr sel);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr receiver, IntPtr sel, IntPtr arg);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendIdx(IntPtr receiver, IntPtr sel, long arg);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendLongArg(IntPtr receiver, IntPtr sel, long arg);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern nint SendLong(IntPtr receiver, IntPtr sel);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void SendBool(IntPtr receiver, IntPtr sel, [MarshalAs(UnmanagedType.I1)] bool arg);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SendReturnBool(IntPtr receiver, IntPtr sel);

    private static IntPtr Cls(string name) => objc_getClass(name);
    private static IntPtr Sel(string name) => sel_registerName(name);

    // --- Linux -------------------------------------------------------------------------------

    private const string Gtk = "libgtk-3.so.0";
    private const string GLib = "libglib-2.0.so.0";

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
    [DllImport(Gtk)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool gtk_window_is_active(IntPtr window);
    [DllImport(Gtk)] private static extern void gtk_window_set_urgency_hint(IntPtr window, [MarshalAs(UnmanagedType.I1)] bool urgent);
    [DllImport(GLib)] private static extern void g_list_free(IntPtr list);
}
