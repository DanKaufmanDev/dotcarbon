using System.Runtime.InteropServices;

namespace DotCarbon.Host.Desktop;

/// <summary>
/// GTK3 system tray implementation. GtkStatusIcon availability depends on the user's desktop
/// environment; unsupported desktops continue without a tray.
/// </summary>
internal static unsafe class LinuxTray
{
    private const string Gtk = "libgtk-3.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const string GLib = "libglib-2.0.so.0";

    private static readonly Dictionary<IntPtr, Action> Handlers = new();
    private static int _nextTag;
    private static IntPtr _menu;
    private static IntPtr _statusIcon;

    // --- runtime mutation (Task 2.3) ---------------------------------------------------------
    // GTK is not thread-safe, so setters are queued onto the GTK main loop with g_idle_add.
    // GtkStatusIcon is icon-only, so SetTitle has no analogue here (see CarbonTrayHandle).

    private static readonly System.Collections.Concurrent.ConcurrentQueue<Action> IdleWork = new();

    public static void SetTooltip(string tooltip) => Post(() =>
    {
        if (_statusIcon != IntPtr.Zero) gtk_status_icon_set_tooltip_text(_statusIcon, tooltip);
    });

    public static void SetVisible(bool visible) => Post(() =>
    {
        if (_statusIcon != IntPtr.Zero) gtk_status_icon_set_visible(_statusIcon, visible);
    });

    public static void Remove() => Post(() =>
    {
        if (_statusIcon == IntPtr.Zero) return;
        gtk_status_icon_set_visible(_statusIcon, false);
        g_object_unref(_statusIcon);
        _statusIcon = IntPtr.Zero;
    });

    private static void Post(Action work)
    {
        IdleWork.Enqueue(work);
        var trampoline = (IntPtr)(delegate* unmanaged<IntPtr, int>)&RunIdleWork;
        g_idle_add(trampoline, IntPtr.Zero);
    }

    [UnmanagedCallersOnly]
    private static int RunIdleWork(IntPtr data)
    {
        while (IdleWork.TryDequeue(out var work))
        {
            try { work(); }
            catch (Exception ex) { Console.Error.WriteLine($"[Carbon] Tray update failed: {ex.Message}"); }
        }
        return 0; // G_SOURCE_REMOVE
    }

    public static void Create(CarbonTrayBuilder builder, Action<CarbonTrayHandle>? onReady = null)
    {
        try
        {
            // Tray setup runs before Photino initializes GTK. Initialize it here and skip cleanly
            // when no display is available.
            if (!gtk_init_check(IntPtr.Zero, IntPtr.Zero))
            {
                Console.Error.WriteLine("[Carbon] System tray: no display connection; skipping tray.");
                return;
            }

            _statusIcon = gtk_status_icon_new();
            gtk_status_icon_set_from_icon_name(_statusIcon, "application-x-executable");
            gtk_status_icon_set_tooltip_text(_statusIcon, builder.Title);
            gtk_status_icon_set_visible(_statusIcon, true);

            _menu = gtk_menu_new();
            foreach (var item in builder.Items)
            {
                IntPtr menuItem;
                if (item.IsSeparator)
                {
                    menuItem = gtk_separator_menu_item_new();
                }
                else
                {
                    menuItem = gtk_menu_item_new_with_label(item.Label!);
                    var tag = (IntPtr)(++_nextTag);
                    Handlers[tag] = item.OnClick!;
                    var activate = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnActivate;
                    g_signal_connect_data(menuItem, "activate", activate, tag, IntPtr.Zero, 0);
                }
                gtk_menu_shell_append(_menu, menuItem);
            }
            gtk_widget_show_all(_menu);

            var popup = (IntPtr)(delegate* unmanaged<IntPtr, uint, uint, IntPtr, void>)&OnPopup;
            g_signal_connect_data(_statusIcon, "popup-menu", popup, IntPtr.Zero, IntPtr.Zero, 0);

            Console.WriteLine($"[Carbon] System tray ready ({builder.Items.Count} item(s)).");
            CarbonTray.NotifyReady(onReady);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Carbon] Failed to create the Linux tray: {ex.Message}");
        }
    }

    [UnmanagedCallersOnly]
    private static void OnActivate(IntPtr item, IntPtr userData)
    {
        if (!Handlers.TryGetValue(userData, out var handler)) return;
        try { handler(); }
        catch (Exception ex) { Console.Error.WriteLine($"[Carbon] Tray handler failed: {ex.Message}"); }
    }

    [UnmanagedCallersOnly]
    private static void OnPopup(IntPtr statusIcon, uint button, uint activateTime, IntPtr userData)
    {
        gtk_menu_popup_at_pointer(_menu, IntPtr.Zero);
    }

    // GTK and GObject interop

    [DllImport(Gtk)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool gtk_init_check(IntPtr argc, IntPtr argv);
    [DllImport(Gtk)] private static extern IntPtr gtk_status_icon_new();
    [DllImport(GObject)] private static extern void g_object_unref(IntPtr obj);
    [DllImport(GLib)] private static extern uint g_idle_add(IntPtr function, IntPtr data);
    [DllImport(Gtk)] private static extern void gtk_status_icon_set_from_icon_name(IntPtr icon, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Gtk)] private static extern void gtk_status_icon_set_tooltip_text(IntPtr icon, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(Gtk)] private static extern void gtk_status_icon_set_visible(IntPtr icon, [MarshalAs(UnmanagedType.I1)] bool visible);
    [DllImport(Gtk)] private static extern IntPtr gtk_menu_new();
    [DllImport(Gtk)] private static extern IntPtr gtk_menu_item_new_with_label([MarshalAs(UnmanagedType.LPUTF8Str)] string label);
    [DllImport(Gtk)] private static extern IntPtr gtk_separator_menu_item_new();
    [DllImport(Gtk)] private static extern void gtk_menu_shell_append(IntPtr menu, IntPtr child);
    [DllImport(Gtk)] private static extern void gtk_widget_show_all(IntPtr widget);
    [DllImport(Gtk)] private static extern void gtk_menu_popup_at_pointer(IntPtr menu, IntPtr triggerEvent);
    [DllImport(GObject)] private static extern ulong g_signal_connect_data(IntPtr instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, IntPtr handler, IntPtr data, IntPtr destroy, int flags);
}
