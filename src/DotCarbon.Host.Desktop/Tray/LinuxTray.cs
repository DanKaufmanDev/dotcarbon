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
    private static Action<CarbonTrayEvent>? _onEvent;
    private static IntPtr _statusIcon;

    // --- runtime mutation (Task 2.3) ---------------------------------------------------------
    // GTK is not thread-safe, so setters are queued onto the GTK main loop with g_idle_add.
    // GtkStatusIcon is icon-only, so SetTitle has no analogue here (see CarbonTrayHandle).

    private static readonly System.Collections.Concurrent.ConcurrentQueue<Action> IdleWork = new();

    public static void SetTooltip(string tooltip) => Post(() =>
    {
        if (_statusIcon != IntPtr.Zero) gtk_status_icon_set_tooltip_text(_statusIcon, tooltip);
    });

    public static void SetIcon(string path) => Post(() =>
    {
        if (_statusIcon != IntPtr.Zero) gtk_status_icon_set_from_file(_statusIcon, path);
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
            if (builder.IconPath is { } iconPath)
                gtk_status_icon_set_from_file(_statusIcon, iconPath);
            else
                gtk_status_icon_set_from_icon_name(_statusIcon, "application-x-executable");
            gtk_status_icon_set_tooltip_text(_statusIcon, builder.Title);
            gtk_status_icon_set_visible(_statusIcon, true);

            _menu = gtk_menu_new();
            FillMenu(_menu, builder.Items);
            gtk_widget_show_all(_menu);

            var popup = (IntPtr)(delegate* unmanaged<IntPtr, uint, uint, IntPtr, void>)&OnPopup;
            g_signal_connect_data(_statusIcon, "popup-menu", popup, IntPtr.Zero, IntPtr.Zero, 0);

            // Task 2.8. GtkStatusIcon only reports button presses — it has no motion or crossing
            // signals, so Enter/Move/Leave are unavailable here (see CarbonTrayEventKind). Both
            // handlers return FALSE so the menu's own popup-menu signal still fires.
            _onEvent = builder.EventHandler;
            if (_onEvent is not null)
            {
                var press = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, int>)&OnButtonEvent;
                g_signal_connect_data(_statusIcon, "button-press-event", press, IntPtr.Zero, IntPtr.Zero, 0);
                g_signal_connect_data(_statusIcon, "button-release-event", press, IntPtr.Zero, IntPtr.Zero, 0);
            }

            Console.WriteLine($"[Carbon] System tray ready ({builder.Items.Count} item(s)).");
            CarbonTray.NotifyReady(onReady);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Carbon] Failed to create the Linux tray: {ex.Message}");
        }
    }

    // --- pointer events (Task 2.8) -----------------------------------------------------------

    // GdkEventType values we care about.
    private const int GdkButtonPress = 4;
    private const int Gdk2ButtonPress = 5;
    private const int GdkButtonRelease = 7;

    /// <summary>
    /// GdkEventButton, as laid out by GDK on 64-bit. Only the fields we read are named; the rest are
    /// padding placeholders so the offsets line up.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct GdkEventButton
    {
        [FieldOffset(0)] public int Type;
        [FieldOffset(48)] public uint State;
        [FieldOffset(52)] public uint Button;
        [FieldOffset(64)] public double XRoot;
        [FieldOffset(72)] public double YRoot;
    }

    [UnmanagedCallersOnly]
    private static int OnButtonEvent(IntPtr statusIcon, IntPtr eventPtr, IntPtr userData)
    {
        // Returning FALSE lets GTK carry on emitting popup-menu, which is what opens the menu.
        try
        {
            if (_onEvent is not { } handler || eventPtr == IntPtr.Zero) return 0;

            var e = Marshal.PtrToStructure<GdkEventButton>(eventPtr);
            // A double click arrives as GDK_2BUTTON_PRESS *in addition to* the plain presses, so
            // reporting it as its own kind rather than a third Click keeps the stream honest.
            var kind = e.Type switch
            {
                Gdk2ButtonPress => CarbonTrayEventKind.DoubleClick,
                GdkButtonPress or GdkButtonRelease => CarbonTrayEventKind.Click,
                _ => (CarbonTrayEventKind?)null,
            };
            if (kind is not { } eventKind) return 0;

            var button = e.Button switch
            {
                2u => CarbonTrayMouseButton.Middle,
                3u => CarbonTrayMouseButton.Right,
                _ => CarbonTrayMouseButton.Left,
            };
            var state = e.Type == GdkButtonRelease ? CarbonTrayButtonState.Up : CarbonTrayButtonState.Down;

            handler(new CarbonTrayEvent(
                eventKind, button, state, new CarbonTrayPoint(e.XRoot, e.YRoot), IconRect()));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Carbon] Tray event failed: {ex.Message}");
        }
        return 0;
    }

    /// <summary>The icon's screen rect. Most modern panels decline to report this.</summary>
    private static CarbonTrayRect IconRect()
    {
        if (_statusIcon == IntPtr.Zero) return default;
        if (!gtk_status_icon_get_geometry(_statusIcon, IntPtr.Zero, out var area, IntPtr.Zero)) return default;
        return new CarbonTrayRect(area.X, area.Y, area.Width, area.Height);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GdkRectangle
    {
        public int X, Y, Width, Height;
    }

    /// <summary>Add tray items to a GtkMenu, recursing into submenus (Task 2.7).</summary>
    private static void FillMenu(IntPtr menu, IReadOnlyList<TrayItem> items)
    {
        foreach (var item in items)
        {
            IntPtr menuItem;
            if (item.IsSeparator)
            {
                menuItem = gtk_separator_menu_item_new();
            }
            else if (item.Children is { } children)
            {
                // A submenu item carries no "activate" handler — GTK opens the attached GtkMenu.
                menuItem = gtk_menu_item_new_with_label(item.Label!);
                var child = gtk_menu_new();
                FillMenu(child, children);
                gtk_menu_item_set_submenu(menuItem, child);
            }
            else
            {
                menuItem = gtk_menu_item_new_with_label(item.Label!);
                var tag = (IntPtr)(++_nextTag);
                Handlers[tag] = item.OnClick!;
                var activate = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnActivate;
                g_signal_connect_data(menuItem, "activate", activate, tag, IntPtr.Zero, 0);
            }
            gtk_menu_shell_append(menu, menuItem);
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
    [DllImport(Gtk)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool gtk_status_icon_get_geometry(IntPtr icon, IntPtr screen, out GdkRectangle area, IntPtr orientation);
    [DllImport(GObject)] private static extern void g_object_unref(IntPtr obj);
    [DllImport(GLib)] private static extern uint g_idle_add(IntPtr function, IntPtr data);
    [DllImport(Gtk)] private static extern void gtk_status_icon_set_from_icon_name(IntPtr icon, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Gtk)] private static extern void gtk_status_icon_set_from_file(IntPtr icon, [MarshalAs(UnmanagedType.LPUTF8Str)] string filename);
    [DllImport(Gtk)] private static extern void gtk_status_icon_set_tooltip_text(IntPtr icon, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(Gtk)] private static extern void gtk_status_icon_set_visible(IntPtr icon, [MarshalAs(UnmanagedType.I1)] bool visible);
    [DllImport(Gtk)] private static extern IntPtr gtk_menu_new();
    [DllImport(Gtk)] private static extern IntPtr gtk_menu_item_new_with_label([MarshalAs(UnmanagedType.LPUTF8Str)] string label);
    [DllImport(Gtk)] private static extern IntPtr gtk_separator_menu_item_new();
    [DllImport(Gtk)] private static extern void gtk_menu_item_set_submenu(IntPtr menuItem, IntPtr submenu);
    [DllImport(Gtk)] private static extern void gtk_menu_shell_append(IntPtr menu, IntPtr child);
    [DllImport(Gtk)] private static extern void gtk_widget_show_all(IntPtr widget);
    [DllImport(Gtk)] private static extern void gtk_menu_popup_at_pointer(IntPtr menu, IntPtr triggerEvent);
    [DllImport(GObject)] private static extern ulong g_signal_connect_data(IntPtr instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, IntPtr handler, IntPtr data, IntPtr destroy, int flags);
}
