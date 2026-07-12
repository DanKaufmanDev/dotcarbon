using DotCarbon.Core.Runtime;

namespace DotCarbon.Host.Desktop;

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

    public CarbonMenuGroupBuilder AddItem(string label, Action onClick, string shortcut = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(onClick);
        _items.Add(new MenuItem(label, onClick, shortcut, IsSeparator: false));
        return this;
    }

    public CarbonMenuGroupBuilder AddSeparator()
    {
        _items.Add(new MenuItem(null, null, string.Empty, IsSeparator: true));
        return this;
    }

    internal MenuGroup Build() => new(_label, _items.ToArray());
}

internal sealed record MenuGroup(string Label, IReadOnlyList<MenuItem> Items);
internal sealed record MenuItem(string? Label, Action? OnClick, string Shortcut, bool IsSeparator);

/// <summary>Desktop native menu entry points.</summary>
public static class DesktopMenuExtensions
{
    /// <summary>
    /// Add a native desktop app menu. macOS is a true application menu; Windows/Linux currently log
    /// a clear unsupported message until window-menu validation lands on those platforms.
    /// </summary>
    public static CarbonApp UseMenu(this CarbonApp app, Action<CarbonMenuBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new CarbonMenuBuilder();
        configure(builder);
        app.Setup(_ => CarbonMenu.Create(builder));
        return app;
    }
}

internal static class CarbonMenu
{
    public static void Create(CarbonMenuBuilder builder)
    {
        if (OperatingSystem.IsMacOS())
            MacMenu.Create(builder);
        else if (OperatingSystem.IsWindows())
            WindowsMenu.Create(builder);
        else if (OperatingSystem.IsLinux())
            LinuxMenu.Create(builder);
        else
            Console.Error.WriteLine("[Carbon] Native app menus are not supported on this platform.");
    }
}
