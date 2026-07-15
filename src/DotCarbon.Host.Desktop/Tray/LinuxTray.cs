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

    private static readonly Dictionary<IntPtr, Action> Handlers = new();
    private static int _nextTag;
    private static IntPtr _menu;
    private static IntPtr _statusIcon;

    public static void Create(CarbonTrayBuilder builder)
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
