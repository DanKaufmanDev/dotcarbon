using DotCarbon.Core.Runtime;

namespace DotCarbon.Host.Desktop;

/// <summary>
/// A standard menu action wired to the platform's own implementation. On macOS these map to native
/// selectors dispatched through the responder chain — which is how the system expects them to work,
/// and notably how ⌘C/⌘V reach the webview at all (AppKit only delivers those key equivalents if
/// matching menu items exist). Windows and Linux have no equivalent roles: <see cref="Quit"/>,
/// <see cref="CloseWindow"/> and <see cref="Minimize"/> are implemented there, and the rest are
/// macOS-only conventions that are ignored (see <c>AddPredefined</c>).
/// </summary>
public enum CarbonMenuRole
{
    Quit,
    About,
    Services,
    Copy,
    Cut,
    Paste,
    SelectAll,
    Undo,
    Redo,
    Minimize,
    Zoom,
    Hide,
    HideOthers,
    ShowAll,
    CloseWindow,
}

/// <summary>Fluent builder for a native desktop app menu.</summary>
public sealed class CarbonMenuBuilder
{
    internal List<MenuGroup> Groups { get; } = [];

    public CarbonMenuBuilder AddMenu(string label, Action<CarbonMenuGroupBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new CarbonMenuGroupBuilder(label);
        configure(builder);
        Groups.Add(builder.Build());
        return this;
    }
}

/// <summary>Fluent builder for one top-level native menu.</summary>
public sealed class CarbonMenuGroupBuilder
{
    private readonly string _label;
    private readonly List<MenuItem> _items = [];

    internal CarbonMenuGroupBuilder(string label)
    {
        _label = label;
    }

    /// <summary>An item. Give it an <paramref name="id"/> to update it later via CarbonMenuHandle.</summary>
    public CarbonMenuGroupBuilder AddItem(string label, Action onClick, string shortcut = "", string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(onClick);
        _items.Add(new MenuItem(label, onClick, EventName: null, Shortcut: shortcut, IsSeparator: false, Id: id));
        return this;
    }

    public CarbonMenuGroupBuilder AddEventItem(string label, string eventName, string shortcut = "", string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        _items.Add(new MenuItem(label, OnClick: null, EventName: eventName, Shortcut: shortcut, IsSeparator: false, Id: id));
        return this;
    }

    /// <summary>A checkable item; toggle it later with <c>CarbonMenuHandle.SetChecked</c>.</summary>
    public CarbonMenuGroupBuilder AddCheckItem(
        string label, Action onClick, bool isChecked = false, string shortcut = "", string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(onClick);
        _items.Add(new MenuItem(label, onClick, EventName: null, Shortcut: shortcut, IsSeparator: false,
            Id: id, IsCheckItem: true, IsChecked: isChecked));
        return this;
    }

    /// <summary>
    /// A standard item handled by the platform (see <see cref="CarbonMenuRole"/>). The label and
    /// shortcut default to the platform's conventions; pass <paramref name="label"/> to override.
    /// An app menu with Copy/Cut/Paste/SelectAll roles is what makes those shortcuts work in the
    /// webview on macOS.
    ///
    /// Note that macOS populates a menu titled "Edit" with items of its own (Emoji &amp; Symbols,
    /// Start Dictation, AutoFill, and an Option-key "Close All" alternate). They are added by AppKit
    /// after the menu is installed, so they will appear alongside whatever is declared here.
    /// </summary>
    public CarbonMenuGroupBuilder AddPredefined(CarbonMenuRole role, string? label = null)
    {
        _items.Add(new MenuItem(label, OnClick: null, EventName: null, Shortcut: string.Empty,
            IsSeparator: false, Role: role));
        return this;
    }

    /// <summary>A nested submenu. Nests to any depth.</summary>
    public CarbonMenuGroupBuilder AddSubmenu(string label, Action<CarbonMenuGroupBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(configure);

        var submenu = new CarbonMenuGroupBuilder(label);
        configure(submenu);
        _items.Add(new MenuItem(label, OnClick: null, EventName: null, Shortcut: string.Empty,
            IsSeparator: false, Children: submenu.Build().Items));
        return this;
    }

    public CarbonMenuGroupBuilder AddSeparator()
    {
        _items.Add(new MenuItem(null, OnClick: null, EventName: null, Shortcut: string.Empty, IsSeparator: true));
        return this;
    }

    internal void Add(MenuItem item) => _items.Add(item);

    internal MenuGroup Build() => new(_label, _items.ToArray());
}

internal sealed record MenuGroup(string Label, IReadOnlyList<MenuItem> Items);

/// <summary>A menu entry. <see cref="Children"/> being non-null makes it a submenu.</summary>
internal sealed record MenuItem(
    string? Label, Action? OnClick, string? EventName, string Shortcut, bool IsSeparator,
    string? Id = null, bool IsCheckItem = false, bool IsChecked = false,
    IReadOnlyList<MenuItem>? Children = null, CarbonMenuRole? Role = null);

/// <summary>
/// A live app menu. Handed to <c>UseMenu</c>'s <c>onReady</c> callback once the native menu exists, so
/// items can be updated while the app runs. Items are addressed by the <c>id</c> given at build time;
/// unknown ids are ignored. Calls marshal to the platform's UI thread, so any thread may call them.
/// </summary>
public sealed class CarbonMenuHandle
{
    /// <summary>Grey an item out (or bring it back).</summary>
    public void SetEnabled(string id, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (OperatingSystem.IsMacOS()) MacMenu.SetEnabled(id, enabled);
        else if (OperatingSystem.IsWindows()) WindowsMenu.SetEnabled(id, enabled);
        else if (OperatingSystem.IsLinux()) LinuxMenu.SetEnabled(id, enabled);
    }

    /// <summary>Tick or untick an item added with <c>AddCheckItem</c>.</summary>
    public void SetChecked(string id, bool isChecked)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (OperatingSystem.IsMacOS()) MacMenu.SetChecked(id, isChecked);
        else if (OperatingSystem.IsWindows()) WindowsMenu.SetChecked(id, isChecked);
        else if (OperatingSystem.IsLinux()) LinuxMenu.SetChecked(id, isChecked);
    }

    /// <summary>Re-label an item.</summary>
    public void SetLabel(string id, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(label);
        if (OperatingSystem.IsMacOS()) MacMenu.SetLabel(id, label);
        else if (OperatingSystem.IsWindows()) WindowsMenu.SetLabel(id, label);
        else if (OperatingSystem.IsLinux()) LinuxMenu.SetLabel(id, label);
    }
}

/// <summary>Desktop native menu entry points.</summary>
public static class DesktopMenuExtensions
{
    /// <summary>
    /// Add a native desktop app menu. macOS installs an application-wide NSMenu; Windows and Linux
    /// attach a menu bar to the main window, so those wait for the window to exist before building.
    /// </summary>
    public static CarbonApp UseMenu(
        this CarbonApp app, Action<CarbonMenuBuilder> configure, Action<CarbonMenuHandle>? onReady = null)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new CarbonMenuBuilder();
        configure(builder);

        // Task 2.10: the frontend's menu:* commands address items in this menu, so they come with it
        // rather than being a separate opt-in.
        app.UsePlugin<MenuPlugin>();

        // macOS's menu belongs to the application, not a window — build it as soon as setup runs.
        if (OperatingSystem.IsMacOS())
        {
            app.Setup(handle => MacMenu.Create(Bind(builder, handle), onReady));
            return app;
        }

        // Linux: Photino won't give us its GtkWindow (WindowHandle is Windows-only), so the backend
        // defers onto the GTK loop and finds the toplevel itself.
        if (OperatingSystem.IsLinux())
        {
            app.Setup(handle => LinuxMenu.Create(Bind(builder, handle), handle.Config.Window.Title, onReady));
            return app;
        }

        // Windows: the menu hangs off the HWND, which only exists once the window has been created.
        var armed = 0;
        app.OnLifecycle(lifecycleEvent =>
        {
            if (lifecycleEvent.Kind != CarbonLifecycleEventKind.WindowCreated ||
                lifecycleEvent.Window is not { } window ||
                window.Label != lifecycleEvent.App.Config.Window.Label ||
                Interlocked.Exchange(ref armed, 1) != 0)
                return;

            try
            {
                CarbonMenu.CreateForWindow(
                    Bind(builder, lifecycleEvent.App), window.Photino().WindowHandle, onReady);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Carbon] Native menu: could not get the window handle: {ex.Message}");
            }
        });
        return app;
    }

    private static CarbonMenuBuilder Bind(CarbonMenuBuilder builder, AppHandle app)
    {
        var bound = new CarbonMenuBuilder();
        foreach (var group in builder.Groups)
        {
            bound.AddMenu(group.Label, menu =>
            {
                foreach (var item in group.Items) menu.Add(BindItem(item, app));
            });
        }
        return bound;
    }

    /// <summary>
    /// Resolve an item's click into a concrete handler, keeping everything else (id, check state,
    /// shortcut) that the backends need to build and later address it. Recurses into submenus.
    /// </summary>
    private static MenuItem BindItem(MenuItem item, AppHandle app)
    {
        // Separators and predefined roles carry no user handler for us to resolve.
        if (item.IsSeparator || item.Role is not null) return item;

        if (item.Children is { } children)
            return item with { Children = children.Select(child => BindItem(child, app)).ToArray() };

        var onClick = item.OnClick ??
            DesktopNativeEventEmitter.Create(app, item.EventName!, item.Label!, "menu");
        return item with { OnClick = onClick, EventName = null };
    }
}

internal static class CarbonMenu
{
    /// <summary>Attach a menu bar to the platform's native window (Windows).</summary>
    public static void CreateForWindow(
        CarbonMenuBuilder builder, IntPtr nativeWindow, Action<CarbonMenuHandle>? onReady = null)
    {
        if (nativeWindow == IntPtr.Zero)
        {
            Console.Error.WriteLine("[Carbon] Native app menu: the window handle was not available.");
            return;
        }

        if (OperatingSystem.IsWindows())
            WindowsMenu.Create(builder, nativeWindow, onReady);
        else
            Console.Error.WriteLine("[Carbon] Native app menus are not supported on this platform.");
    }

    /// <summary>Invoke the ready callback without letting a user exception kill the UI thread.</summary>
    internal static void NotifyReady(Action<CarbonMenuHandle>? onReady)
    {
        if (onReady is null) return;
        try { onReady(new CarbonMenuHandle()); }
        catch (Exception ex) { Console.Error.WriteLine($"[Carbon] Menu onReady failed: {ex.Message}"); }
    }
}
