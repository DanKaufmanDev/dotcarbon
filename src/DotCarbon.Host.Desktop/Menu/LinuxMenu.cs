using System.Runtime.InteropServices;

namespace DotCarbon.Host.Desktop;

/// <summary>
/// Linux application menu: a GtkMenuBar packed above Photino's webview.
///
/// Photino puts the webview directly inside the GtkWindow, and a GtkWindow (a GtkBin) holds exactly
/// one child — so to make room for a menu bar we re-parent: take the webview out, put a vertical
/// GtkBox in the window, then pack [menu bar, webview] into it. Menu clicks arrive on the GTK main
/// loop via each item's "activate" signal.
/// </summary>
internal static unsafe class LinuxMenu
{
    private const string Gtk = "libgtk-3.so.0";
    private const string GObject = "libgobject-2.0.so.0";

    private const int GtkOrientationVertical = 1;

    private static readonly Dictionary<IntPtr, Action> Handlers = new();
    private static int _nextTag;

    public static void Create(CarbonMenuBuilder builder, IntPtr window)
    {
        try
        {
            // Built from the window-created callback, which already runs on the GTK thread with a
            // display connection; gtk_init_check is idempotent insurance.
            if (!gtk_init_check(IntPtr.Zero, IntPtr.Zero))
            {
                Console.Error.WriteLine("[Carbon] Native menu: no display connection; skipping.");
                return;
            }

            var content = gtk_bin_get_child(window);
            if (content == IntPtr.Zero)
            {
                Console.Error.WriteLine("[Carbon] Native menu: the window has no content widget to re-parent.");
                return;
            }

            var menuBar = BuildMenuBar(builder);

            // Keep the webview alive while it is detached from the window.
            g_object_ref(content);
            gtk_container_remove(window, content);

            var box = gtk_box_new(GtkOrientationVertical, 0);
            gtk_box_pack_start(box, menuBar, expand: false, fill: false, padding: 0);
            gtk_box_pack_start(box, content, expand: true, fill: true, padding: 0);
            g_object_unref(content);

            gtk_container_add(window, box);
            gtk_widget_show_all(window);

            Console.WriteLine($"[Carbon] Native menu ready ({builder.Groups.Count} top-level menu(s)).");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Carbon] Failed to create the Linux menu: {ex.Message}");
        }
    }

    private static IntPtr BuildMenuBar(CarbonMenuBuilder builder)
    {
        var menuBar = gtk_menu_bar_new();
        foreach (var group in builder.Groups)
        {
            var root = gtk_menu_item_new_with_label(group.Label);
            var submenu = gtk_menu_new();

            foreach (var item in group.Items)
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
                gtk_menu_shell_append(submenu, menuItem);
            }

            gtk_menu_item_set_submenu(root, submenu);
            gtk_menu_shell_append(menuBar, root);
        }
        return menuBar;
    }

    [UnmanagedCallersOnly]
    private static void OnActivate(IntPtr item, IntPtr userData)
    {
        if (!Handlers.TryGetValue(userData, out var handler)) return;
        try { handler(); }
        catch (Exception ex) { Console.Error.WriteLine($"[Carbon] Menu handler failed: {ex.Message}"); }
    }

    // --- GTK / GObject interop ---------------------------------------------------------------

    [DllImport(Gtk)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool gtk_init_check(IntPtr argc, IntPtr argv);
    [DllImport(Gtk)] private static extern IntPtr gtk_bin_get_child(IntPtr bin);
    [DllImport(Gtk)] private static extern void gtk_container_remove(IntPtr container, IntPtr widget);
    [DllImport(Gtk)] private static extern void gtk_container_add(IntPtr container, IntPtr widget);
    [DllImport(Gtk)] private static extern IntPtr gtk_box_new(int orientation, int spacing);
    [DllImport(Gtk)] private static extern void gtk_box_pack_start(IntPtr box, IntPtr child,
        [MarshalAs(UnmanagedType.I1)] bool expand, [MarshalAs(UnmanagedType.I1)] bool fill, uint padding);
    [DllImport(Gtk)] private static extern IntPtr gtk_menu_bar_new();
    [DllImport(Gtk)] private static extern IntPtr gtk_menu_new();
    [DllImport(Gtk)] private static extern IntPtr gtk_menu_item_new_with_label([MarshalAs(UnmanagedType.LPUTF8Str)] string label);
    [DllImport(Gtk)] private static extern IntPtr gtk_separator_menu_item_new();
    [DllImport(Gtk)] private static extern void gtk_menu_item_set_submenu(IntPtr menuItem, IntPtr submenu);
    [DllImport(Gtk)] private static extern void gtk_menu_shell_append(IntPtr menuShell, IntPtr child);
    [DllImport(Gtk)] private static extern void gtk_widget_show_all(IntPtr widget);
    [DllImport(GObject)] private static extern IntPtr g_object_ref(IntPtr obj);
    [DllImport(GObject)] private static extern void g_object_unref(IntPtr obj);
    [DllImport(GObject)] private static extern ulong g_signal_connect_data(IntPtr instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, IntPtr handler, IntPtr data, IntPtr destroy, int flags);
}
