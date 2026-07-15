using DotCarbon.Core.Runtime;

namespace DotCarbon.Host.Desktop;

/// <summary>Fluent builder for a system tray icon and its menu.</summary>
public sealed class CarbonTrayBuilder
{
    internal string Title { get; private set; } = "●";
    internal string? IconPath { get; private set; }
    internal bool IconIsTemplate { get; private set; }
    internal List<TrayItem> Items { get; } = [];

    /// <summary>The tray button text (an emoji or short glyph reads best in the menu bar).</summary>
    public CarbonTrayBuilder SetTitle(string title)
    {
        Title = title;
        return this;
    }

    /// <summary>
    /// The tray icon image. PNG works on macOS and Linux; Windows needs an <c>.ico</c> (other formats
    /// are decoded through GDI+ as a fallback). <paramref name="isTemplate"/> is macOS-only: a template
    /// image is drawn as a mask so it adapts to light/dark menu bars — the same idea as Tauri's
    /// <c>icon_as_template</c>. Recommended over <see cref="SetTitle"/> for a real app.
    /// </summary>
    public CarbonTrayBuilder SetIcon(string path, bool isTemplate = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        IconPath = Path.GetFullPath(path);
        IconIsTemplate = isTemplate;
        return this;
    }

    public CarbonTrayBuilder AddItem(string label, Action onClick)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(onClick);
        Items.Add(new TrayItem(label, onClick, EventName: null, IsSeparator: false));
        return this;
    }

    public CarbonTrayBuilder AddEventItem(string label, string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        Items.Add(new TrayItem(label, OnClick: null, EventName: eventName, IsSeparator: false));
        return this;
    }

    /// <summary>A nested submenu in the tray menu. Nests to any depth.</summary>
    public CarbonTrayBuilder AddSubmenu(string label, Action<CarbonTrayBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(configure);

        var submenu = new CarbonTrayBuilder();
        configure(submenu);
        Items.Add(new TrayItem(label, OnClick: null, EventName: null, IsSeparator: false,
            Children: submenu.Items.ToArray()));
        return this;
    }

    public CarbonTrayBuilder AddSeparator()
    {
        Items.Add(new TrayItem(null, OnClick: null, EventName: null, IsSeparator: true));
        return this;
    }

    internal void Add(TrayItem item) => Items.Add(item);

    internal CarbonTrayBuilder Bind(AppHandle app)
    {
        var bound = new CarbonTrayBuilder().SetTitle(Title);
        if (IconPath is { } icon) bound.SetIcon(icon, IconIsTemplate);
        foreach (var item in Items) bound.Add(BindItem(item, app));
        return bound;
    }

    /// <summary>Resolve clicks into concrete handlers, recursing into submenus.</summary>
    private static TrayItem BindItem(TrayItem item, AppHandle app)
    {
        if (item.IsSeparator) return item;

        if (item.Children is { } children)
            return item with { Children = children.Select(child => BindItem(child, app)).ToArray() };

        var onClick = item.OnClick ??
            DesktopNativeEventEmitter.Create(app, item.EventName!, item.Label!, "tray");
        return item with { OnClick = onClick, EventName = null };
    }
}

/// <summary>A tray menu entry. <see cref="Children"/> being non-null makes it a submenu.</summary>
internal sealed record TrayItem(
    string? Label, Action? OnClick, string? EventName, bool IsSeparator,
    IReadOnlyList<TrayItem>? Children = null);

/// <summary>
/// A live tray icon. Handed to <c>UseTray</c>'s <c>onReady</c> callback once the native icon exists,
/// so the tray can be updated while the app runs instead of being create-once. Every call marshals to
/// the platform's UI thread, so it is safe to call from anywhere.
/// </summary>
public sealed class CarbonTrayHandle
{
    /// <summary>
    /// The tray's text. macOS shows this next to the icon in the menu bar; Windows and Linux trays
    /// are icon-only, where this is a no-op (same shape as Tauri's macOS-only <c>set_title</c>).
    /// </summary>
    public void SetTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (OperatingSystem.IsMacOS()) MacTray.SetTitle(title);
    }

    /// <summary>
    /// Swap the tray icon. Same format rules as <see cref="CarbonTrayBuilder.SetIcon"/>.
    /// </summary>
    public void SetIcon(string path, bool isTemplate = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        if (OperatingSystem.IsMacOS()) MacTray.SetIcon(full, isTemplate);
        else if (OperatingSystem.IsWindows()) WindowsTray.SetIcon(full);
        else if (OperatingSystem.IsLinux()) LinuxTray.SetIcon(full);
    }

    /// <summary>The hover tooltip.</summary>
    public void SetTooltip(string tooltip)
    {
        ArgumentNullException.ThrowIfNull(tooltip);
        if (OperatingSystem.IsMacOS()) MacTray.SetTooltip(tooltip);
        else if (OperatingSystem.IsWindows()) WindowsTray.SetTooltip(tooltip);
        else if (OperatingSystem.IsLinux()) LinuxTray.SetTooltip(tooltip);
    }

    /// <summary>Show or hide the icon without tearing it down.</summary>
    public void SetVisible(bool visible)
    {
        if (OperatingSystem.IsMacOS()) MacTray.SetVisible(visible);
        else if (OperatingSystem.IsWindows()) WindowsTray.SetVisible(visible);
        else if (OperatingSystem.IsLinux()) LinuxTray.SetVisible(visible);
    }

    /// <summary>Remove the icon entirely.</summary>
    public void Remove()
    {
        if (OperatingSystem.IsMacOS()) MacTray.Remove();
        else if (OperatingSystem.IsWindows()) WindowsTray.Remove();
        else if (OperatingSystem.IsLinux()) LinuxTray.Remove();
    }
}

/// <summary>Desktop tray entry points.</summary>
public static class DesktopTrayExtensions
{
    /// <summary>
    /// Adds a system tray icon and menu when the desktop app starts. <paramref name="onReady"/> runs
    /// once the native icon exists and receives a handle for updating it later.
    /// </summary>
    public static CarbonApp UseTray(
        this CarbonApp app, Action<CarbonTrayBuilder> configure, Action<CarbonTrayHandle>? onReady = null)
    {
        var builder = new CarbonTrayBuilder();
        configure(builder);
        app.Setup(handle => CarbonTray.Create(builder.Bind(handle), onReady));
        return app;
    }
}

internal static class CarbonTray
{
    public static void Create(CarbonTrayBuilder builder, Action<CarbonTrayHandle>? onReady = null)
    {
        if (OperatingSystem.IsMacOS())
            MacTray.Create(builder, onReady);
        else if (OperatingSystem.IsWindows())
            WindowsTray.Create(builder, onReady);
        else if (OperatingSystem.IsLinux())
            LinuxTray.Create(builder, onReady);
        else
            Console.Error.WriteLine("[Carbon] System tray is not supported on this platform.");
    }

    /// <summary>Invoke the ready callback without letting a user exception kill the UI thread.</summary>
    internal static void NotifyReady(Action<CarbonTrayHandle>? onReady)
    {
        if (onReady is null) return;
        try { onReady(new CarbonTrayHandle()); }
        catch (Exception ex) { Console.Error.WriteLine($"[Carbon] Tray onReady failed: {ex.Message}"); }
    }
}
