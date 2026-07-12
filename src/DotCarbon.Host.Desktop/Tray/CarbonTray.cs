using DotCarbon.Core.Runtime;

namespace DotCarbon.Host.Desktop;

/// <summary>Fluent builder for a system tray icon and its menu.</summary>
public sealed class CarbonTrayBuilder
{
    internal string Title { get; private set; } = "●";
    internal List<TrayItem> Items { get; } = [];

    /// <summary>The tray button text (an emoji or short glyph reads best in the menu bar).</summary>
    public CarbonTrayBuilder SetTitle(string title)
    {
        Title = title;
        return this;
    }

    public CarbonTrayBuilder AddItem(string label, Action onClick)
    {
        Items.Add(new TrayItem(label, onClick, IsSeparator: false));
        return this;
    }

    public CarbonTrayBuilder AddSeparator()
    {
        Items.Add(new TrayItem(null, null, IsSeparator: true));
        return this;
    }
}

internal sealed record TrayItem(string? Label, Action? OnClick, bool IsSeparator);

/// <summary>Desktop tray entry points.</summary>
public static class DesktopTrayExtensions
{
    /// <summary>
    /// Add a system tray icon + menu. The tray is created on the main thread when the app starts.
    /// macOS is verified; Windows/Linux are implemented but not yet runtime-tested (see roadmap).
    /// </summary>
    public static CarbonApp UseTray(this CarbonApp app, Action<CarbonTrayBuilder> configure)
    {
        var builder = new CarbonTrayBuilder();
        configure(builder);
        app.Setup(_ => CarbonTray.Create(builder));
        return app;
    }
}

internal static class CarbonTray
{
    public static void Create(CarbonTrayBuilder builder)
    {
        if (OperatingSystem.IsMacOS())
            MacTray.Create(builder);
        else if (OperatingSystem.IsWindows())
            WindowsTray.Create(builder);
        else if (OperatingSystem.IsLinux())
            LinuxTray.Create(builder);
        else
            Console.Error.WriteLine("[Carbon] System tray is not supported on this platform.");
    }
}
