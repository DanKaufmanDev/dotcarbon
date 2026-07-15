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

    public CarbonTrayBuilder AddSeparator()
    {
        Items.Add(new TrayItem(null, OnClick: null, EventName: null, IsSeparator: true));
        return this;
    }

    internal CarbonTrayBuilder Bind(AppHandle app)
    {
        var bound = new CarbonTrayBuilder().SetTitle(Title);
        foreach (var item in Items)
        {
            if (item.IsSeparator)
            {
                bound.AddSeparator();
                continue;
            }

            var onClick = item.OnClick ??
                DesktopNativeEventEmitter.Create(app, item.EventName!, item.Label!, "tray");
            bound.AddItem(item.Label!, onClick);
        }
        return bound;
    }
}

internal sealed record TrayItem(string? Label, Action? OnClick, string? EventName, bool IsSeparator);

/// <summary>Desktop tray entry points.</summary>
public static class DesktopTrayExtensions
{
    /// <summary>
    /// Adds a system tray icon and menu when the desktop app starts.
    /// </summary>
    public static CarbonApp UseTray(this CarbonApp app, Action<CarbonTrayBuilder> configure)
    {
        var builder = new CarbonTrayBuilder();
        configure(builder);
        app.Setup(handle => CarbonTray.Create(builder.Bind(handle)));
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
