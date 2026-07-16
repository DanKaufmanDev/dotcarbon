using System.Runtime.InteropServices;

namespace DotCarbon.Host.Desktop;

/// <summary>
/// Linux tray via StatusNotifierItem, using libayatana-appindicator (Task 2.9).
///
/// GtkStatusIcon draws into the system tray area of the X11 panel, which stock GNOME has not had for
/// years — the icon simply never appears. StatusNotifierItem is the D-Bus protocol that replaced it:
/// the app publishes its icon and menu on the bus and the desktop's panel renders them. This is the
/// only path that works on modern GNOME, and it is what Tauri uses.
///
/// The trade-off is that the panel — not the app — owns the icon, so there are no click events to
/// report and no tooltip to set. That matches Tauri, whose TrayIconEvent docs say of Linux:
/// "Unsupported. The event is not emitted even though the icon is shown and will still show a context
/// menu on right click."
///
/// The library is a runtime dependency the user's distro may not have, so everything here degrades to
/// "not available" rather than throwing, and <see cref="LinuxTray"/> falls back to GtkStatusIcon.
/// </summary>
internal static class LinuxAppIndicator
{
    private const string AppIndicator = "libayatana-appindicator3.so.1";

    private const int CategoryApplicationStatus = 0;
    private const int StatusPassive = 0;
    private const int StatusActive = 1;

    private static IntPtr _indicator;
    private static bool _probed;
    private static bool _available;

    /// <summary>Whether libayatana-appindicator can be loaded at all.</summary>
    public static bool IsAvailable
    {
        get
        {
            if (_probed) return _available;
            _probed = true;
            // Probing keeps a missing library a fallback rather than a DllNotFoundException on the
            // first call. A successful load also satisfies the DllImports below.
            _available = NativeLibrary.TryLoad(AppIndicator, out _);
            return _available;
        }
    }

    /// <summary>
    /// Publish the tray on the session bus. <paramref name="menu"/> is an already-built GtkMenu, which
    /// the indicator exports over D-Bus for the panel to render. Returns false to let the caller fall
    /// back to GtkStatusIcon.
    /// </summary>
    public static bool TryCreate(CarbonTrayBuilder builder, IntPtr menu)
    {
        if (!IsAvailable) return false;

        try
        {
            // The id must be stable: the panel keys its per-app state off it.
            _indicator = app_indicator_new("dotcarbon-tray", "application-x-executable",
                CategoryApplicationStatus);
            if (_indicator == IntPtr.Zero) return false;

            if (builder.IconPath is { } iconPath) ApplyIcon(iconPath);
            app_indicator_set_title(_indicator, builder.Title);
            app_indicator_set_menu(_indicator, menu);
            // An indicator is Passive (hidden) until it is set Active.
            app_indicator_set_status(_indicator, StatusActive);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Carbon] Tray: appindicator setup failed ({ex.Message}); falling back.");
            _indicator = IntPtr.Zero;
            return false;
        }
    }

    public static void SetIcon(string path)
    {
        if (_indicator == IntPtr.Zero) return;
        ApplyIcon(path);
    }

    /// <summary>
    /// The indicator takes a themed icon *name*, not a path, so point the theme at the file's
    /// directory and name the icon by its stem — the same trick Tauri uses.
    /// </summary>
    private static void ApplyIcon(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is null) return;
        app_indicator_set_icon_theme_path(_indicator, directory);
        app_indicator_set_icon_full(_indicator, Path.GetFileNameWithoutExtension(path), string.Empty);
    }

    public static void SetVisible(bool visible)
    {
        if (_indicator == IntPtr.Zero) return;
        app_indicator_set_status(_indicator, visible ? StatusActive : StatusPassive);
    }

    public static void Remove()
    {
        if (_indicator == IntPtr.Zero) return;
        app_indicator_set_status(_indicator, StatusPassive);
        _indicator = IntPtr.Zero;
    }

    [DllImport(AppIndicator)] private static extern IntPtr app_indicator_new(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string id, [MarshalAs(UnmanagedType.LPUTF8Str)] string iconName, int category);
    [DllImport(AppIndicator)] private static extern void app_indicator_set_status(IntPtr indicator, int status);
    [DllImport(AppIndicator)] private static extern void app_indicator_set_menu(IntPtr indicator, IntPtr menu);
    [DllImport(AppIndicator)] private static extern void app_indicator_set_title(
        IntPtr indicator, [MarshalAs(UnmanagedType.LPUTF8Str)] string title);
    [DllImport(AppIndicator)] private static extern void app_indicator_set_icon_theme_path(
        IntPtr indicator, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(AppIndicator)] private static extern void app_indicator_set_icon_full(
        IntPtr indicator, [MarshalAs(UnmanagedType.LPUTF8Str)] string iconName, [MarshalAs(UnmanagedType.LPUTF8Str)] string description);
}
