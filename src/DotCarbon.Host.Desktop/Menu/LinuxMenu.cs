using System.Runtime.InteropServices;

namespace DotCarbon.Host.Desktop;

/// <summary>
/// Linux application menu: a GtkMenuBar packed above Photino's webview.
///
/// Photino does not hand us its GtkWindow (its <c>WindowHandle</c> is Windows-only and throws
/// elsewhere), and the window only exists once Photino's GTK loop is running. So we defer onto the
/// GTK main loop with g_idle_add and find the toplevel ourselves via gtk_window_list_toplevels.
///
/// A GtkWindow is a GtkBin — exactly one child, which Photino uses for the webview — so making room
/// for a menu bar means re-parenting: take the webview out, put a vertical GtkBox in the window, and
/// pack [menu bar, webview] into it. Clicks arrive via each item's "activate" signal.
/// </summary>
internal static unsafe class LinuxMenu
{
    private const string Gtk = "libgtk-3.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const string GLib = "libglib-2.0.so.0";

    private const int GtkOrientationVertical = 1;
    private const int GSourceRemove = 0;

    private static readonly Dictionary<IntPtr, Action> Handlers = new();
    private static readonly Dictionary<string, IntPtr> ItemsById = new(StringComparer.Ordinal);
    private static readonly System.Collections.Concurrent.ConcurrentQueue<Action> IdleWork = new();
    private static int _nextTag;
    private static CarbonMenuBuilder? _pending;
    private static string? _pendingTitle;
    private static Action<CarbonMenuHandle>? _onReady;
    private static IntPtr _window;
    private static IntPtr _box;      // the vertical box holding [menu bar, webview]
    private static IntPtr _menuBar;

    // --- runtime mutation (Task 2.4) ---------------------------------------------------------
    // GTK is not thread-safe, so setters are queued onto the GTK loop with g_idle_add.

    public static void SetEnabled(string id, bool enabled) => Post(() =>
    {
        if (ItemsById.TryGetValue(id, out var item)) gtk_widget_set_sensitive(item, enabled);
    });

    public static void SetChecked(string id, bool isChecked) => Post(() =>
    {
        if (ItemsById.TryGetValue(id, out var item)) gtk_check_menu_item_set_active(item, isChecked);
    });

    public static void SetLabel(string id, string label) => Post(() =>
    {
        if (ItemsById.TryGetValue(id, out var item)) gtk_menu_item_set_label(item, label);
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
            catch (Exception ex) { Console.Error.WriteLine($"[Carbon] Menu update failed: {ex.Message}"); }
        }
        return GSourceRemove;
    }

    /// <summary>Queue the menu build onto the GTK loop; <paramref name="windowTitle"/> disambiguates
    /// the toplevel when more than one exists.</summary>
    public static void Create(
        CarbonMenuBuilder builder, string? windowTitle, Action<CarbonMenuHandle>? onReady = null)
    {
        _pending = builder;
        _pendingTitle = windowTitle;
        _onReady = onReady;
        var idle = (IntPtr)(delegate* unmanaged<IntPtr, int>)&OnIdleCreate;
        g_idle_add(idle, IntPtr.Zero);
    }

    [UnmanagedCallersOnly]
    private static int OnIdleCreate(IntPtr data)
    {
        var builder = _pending;
        _pending = null;
        if (builder is not null) CreateNow(builder, _pendingTitle);
        CarbonMenu.NotifyReady(_onReady);
        _onReady = null;
        return GSourceRemove;
    }

    private static void CreateNow(CarbonMenuBuilder builder, string? title)
    {
        try
        {
            var window = FindWindow(title);
            if (window == IntPtr.Zero)
            {
                Console.Error.WriteLine("[Carbon] Native menu: could not find the application window.");
                return;
            }

            var content = gtk_bin_get_child(window);
            if (content == IntPtr.Zero)
            {
                Console.Error.WriteLine("[Carbon] Native menu: the window has no content widget to re-parent.");
                return;
            }

            // Predefined roles act on the window, so it must be known before the items are built.
            _window = window;
            var menuBar = BuildMenuBar(builder);

            // Keep the webview alive while it is detached from the window.
            g_object_ref(content);
            gtk_container_remove(window, content);

            var box = gtk_box_new(GtkOrientationVertical, 0);
            gtk_box_pack_start(box, menuBar, expand: false, fill: false, padding: 0);
            gtk_box_pack_start(box, content, expand: true, fill: true, padding: 0);
            g_object_unref(content);
            _box = box;
            _menuBar = menuBar;

            gtk_container_add(window, box);
            gtk_widget_show_all(window);

            Console.WriteLine($"[Carbon] Native menu ready ({builder.Groups.Count} top-level menu(s)).");
            Console.Out.Flush();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Carbon] Failed to create the Linux menu: {ex.Message}");
        }
    }

    /// <summary>
    /// Replace the whole menu bar (Task 2.11). The webview is already packed into our box, so unlike
    /// the first build there is no re-parenting to redo — the old bar is destroyed and a new one
    /// packed in its place, reordered to the top so it lands above the webview rather than below it.
    /// Queued onto the GTK loop like every other mutation here.
    /// </summary>
    public static void Rebuild(CarbonMenuBuilder builder) => Post(() =>
    {
        if (_box == IntPtr.Zero)
        {
            Console.Error.WriteLine("[Carbon] Native menu: no menu bar to rebuild.");
            return;
        }

        Handlers.Clear();
        ItemsById.Clear();
        _nextTag = 0;

        var menuBar = BuildMenuBar(builder);
        if (_menuBar != IntPtr.Zero) gtk_widget_destroy(_menuBar);
        gtk_box_pack_start(_box, menuBar, expand: false, fill: false, padding: 0);
        gtk_box_reorder_child(_box, menuBar, 0);
        _menuBar = menuBar;
        gtk_widget_show_all(_box);
    });

    /// <summary>The Photino window: prefer a title match, else the first toplevel that has content.</summary>
    private static IntPtr FindWindow(string? title)
    {
        var list = gtk_window_list_toplevels();
        var fallback = IntPtr.Zero;
        try
        {
            for (var node = list; node != IntPtr.Zero; node = Marshal.ReadIntPtr(node, IntPtr.Size))
            {
                var window = Marshal.ReadIntPtr(node);
                if (window == IntPtr.Zero || gtk_bin_get_child(window) == IntPtr.Zero) continue;
                if (fallback == IntPtr.Zero) fallback = window;

                if (string.IsNullOrEmpty(title)) continue;
                var current = Marshal.PtrToStringUTF8(gtk_window_get_title(window));
                if (string.Equals(current, title, StringComparison.Ordinal)) return window;
            }
        }
        finally
        {
            if (list != IntPtr.Zero) g_list_free(list);
        }
        return fallback;
    }

    private static IntPtr BuildMenuBar(CarbonMenuBuilder builder)
    {
        var menuBar = gtk_menu_bar_new();
        foreach (var group in builder.Groups)
        {
            var root = gtk_menu_item_new_with_label(group.Label);
            var submenu = gtk_menu_new();
            FillMenu(submenu, group.Items);
            gtk_menu_item_set_submenu(root, submenu);
            gtk_menu_shell_append(menuBar, root);
        }
        return menuBar;
    }

    /// <summary>Add items to a GtkMenu, recursing into submenus (Task 2.7).</summary>
    private static void FillMenu(IntPtr menu, IReadOnlyList<MenuItem> items)
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
                if (item.Id is { } submenuId) ItemsById[submenuId] = menuItem;
            }
            else if (item.Role is { } role)
            {
                // GTK has no menu roles; only the window/app actions have a sensible mapping.
                var predefined = PredefinedRole(role, item.Label);
                if (predefined is null) continue;
                menuItem = gtk_menu_item_new_with_label(predefined.Value.Label);
                var roleTag = (IntPtr)(++_nextTag);
                Handlers[roleTag] = predefined.Value.Action;
                var roleActivate = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnActivate;
                g_signal_connect_data(menuItem, "activate", roleActivate, roleTag, IntPtr.Zero, 0);
            }
            else
            {
                menuItem = item.IsCheckItem
                    ? gtk_check_menu_item_new_with_label(item.Label!)
                    : gtk_menu_item_new_with_label(item.Label!);
                if (item.IsCheckItem && item.IsChecked)
                    gtk_check_menu_item_set_active(menuItem, true);
                if (item.Id is { } itemId)
                    ItemsById[itemId] = menuItem;

                var tag = (IntPtr)(++_nextTag);
                Handlers[tag] = item.OnClick!;
                var activate = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnActivate;
                g_signal_connect_data(menuItem, "activate", activate, tag, IntPtr.Zero, 0);
            }
            gtk_menu_shell_append(menu, menuItem);
        }
    }

    /// <summary>
    /// GTK has no predefined menu roles. Quit, CloseWindow and Minimize map onto the window; the rest
    /// (Copy/Paste/Hide/Services/About) are macOS conventions with no GTK analogue and are skipped
    /// rather than rendered as dead items. Clipboard shortcuts already work in WebKitGTK natively.
    /// </summary>
    private static (string Label, Action Action)? PredefinedRole(CarbonMenuRole role, string? label) => role switch
    {
        CarbonMenuRole.Quit => (label ?? "Quit", () => { if (_window != IntPtr.Zero) gtk_window_close(_window); }),
        CarbonMenuRole.CloseWindow => (label ?? "Close", () => { if (_window != IntPtr.Zero) gtk_window_close(_window); }),
        CarbonMenuRole.Minimize => (label ?? "Minimize", () => { if (_window != IntPtr.Zero) gtk_window_iconify(_window); }),
        _ => null,
    };

    [UnmanagedCallersOnly]
    private static void OnActivate(IntPtr item, IntPtr userData)
    {
        if (!Handlers.TryGetValue(userData, out var handler)) return;
        try { handler(); }
        catch (Exception ex) { Console.Error.WriteLine($"[Carbon] Menu handler failed: {ex.Message}"); }
    }

    // --- GTK / GObject / GLib interop --------------------------------------------------------

    [DllImport(Gtk)] private static extern IntPtr gtk_window_list_toplevels();
    [DllImport(Gtk)] private static extern IntPtr gtk_window_get_title(IntPtr window);
    [DllImport(Gtk)] private static extern IntPtr gtk_bin_get_child(IntPtr bin);
    [DllImport(Gtk)] private static extern void gtk_container_remove(IntPtr container, IntPtr widget);
    [DllImport(Gtk)] private static extern void gtk_container_add(IntPtr container, IntPtr widget);
    [DllImport(Gtk)] private static extern IntPtr gtk_box_new(int orientation, int spacing);
    [DllImport(Gtk)] private static extern void gtk_box_pack_start(IntPtr box, IntPtr child,
        [MarshalAs(UnmanagedType.I1)] bool expand, [MarshalAs(UnmanagedType.I1)] bool fill, uint padding);
    [DllImport(Gtk)] private static extern IntPtr gtk_menu_bar_new();
    [DllImport(Gtk)] private static extern void gtk_widget_destroy(IntPtr widget);
    [DllImport(Gtk)] private static extern void gtk_box_reorder_child(IntPtr box, IntPtr child, int position);
    [DllImport(Gtk)] private static extern void gtk_window_close(IntPtr window);
    [DllImport(Gtk)] private static extern void gtk_window_iconify(IntPtr window);
    [DllImport(Gtk)] private static extern IntPtr gtk_menu_new();
    [DllImport(Gtk)] private static extern IntPtr gtk_menu_item_new_with_label([MarshalAs(UnmanagedType.LPUTF8Str)] string label);
    [DllImport(Gtk)] private static extern IntPtr gtk_check_menu_item_new_with_label([MarshalAs(UnmanagedType.LPUTF8Str)] string label);
    [DllImport(Gtk)] private static extern void gtk_check_menu_item_set_active(IntPtr item, [MarshalAs(UnmanagedType.I1)] bool active);
    [DllImport(Gtk)] private static extern void gtk_menu_item_set_label(IntPtr item, [MarshalAs(UnmanagedType.LPUTF8Str)] string label);
    [DllImport(Gtk)] private static extern void gtk_widget_set_sensitive(IntPtr widget, [MarshalAs(UnmanagedType.I1)] bool sensitive);
    [DllImport(Gtk)] private static extern IntPtr gtk_separator_menu_item_new();
    [DllImport(Gtk)] private static extern void gtk_menu_item_set_submenu(IntPtr menuItem, IntPtr submenu);
    [DllImport(Gtk)] private static extern void gtk_menu_shell_append(IntPtr menuShell, IntPtr child);
    [DllImport(Gtk)] private static extern void gtk_widget_show_all(IntPtr widget);
    [DllImport(GObject)] private static extern IntPtr g_object_ref(IntPtr obj);
    [DllImport(GObject)] private static extern void g_object_unref(IntPtr obj);
    [DllImport(GObject)] private static extern ulong g_signal_connect_data(IntPtr instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, IntPtr handler, IntPtr data, IntPtr destroy, int flags);
    [DllImport(GLib)] private static extern uint g_idle_add(IntPtr function, IntPtr data);
    [DllImport(GLib)] private static extern void g_list_free(IntPtr list);
}
