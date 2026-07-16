namespace DotCarbon.Host.Desktop;

/// <summary>
/// A menu item described as data rather than built with the fluent builder (Task 2.11). This is what
/// the frontend sends when it declares a menu, and it is deliberately a mirror of what
/// <see cref="CarbonMenuGroupBuilder"/> can express.
///
/// <see cref="Items"/> being non-null makes it a submenu; <see cref="Separator"/> makes it a divider;
/// <see cref="Role"/> makes it a predefined platform item. Items the frontend declares have no C#
/// handler to run, so a click is reported back as an event — see <see cref="MenuSpec.ToBuilder"/>.
/// </summary>
public sealed record CarbonMenuItemSpec(
    string? Id = null,
    string? Label = null,
    bool Separator = false,
    bool? Checked = null,
    bool? Enabled = null,
    string? Role = null,
    string Shortcut = "",
    IReadOnlyList<CarbonMenuItemSpec>? Items = null);

/// <summary>One top-level menu in the app menu bar.</summary>
public sealed record CarbonMenuGroupSpec(string Label, IReadOnlyList<CarbonMenuItemSpec> Items);

/// <summary>Turns declared menu data into the builders the native backends already understand.</summary>
internal static class MenuSpec
{
    /// <summary>
    /// The event a frontend-declared app menu item raises when clicked. Fixed rather than per-item,
    /// so a frontend registers one listener and switches on the item id.
    /// </summary>
    public const string MenuItemEvent = "menu:item_clicked";

    /// <summary>The tray equivalent of <see cref="MenuItemEvent"/>.</summary>
    public const string TrayItemEvent = "tray:item_clicked";

    /// <summary>Pour declared groups into a builder, so the handle's usual bind/rebuild path runs.</summary>
    public static void Fill(CarbonMenuBuilder builder, IReadOnlyList<CarbonMenuGroupSpec> groups)
    {
        foreach (var group in groups)
        {
            builder.AddMenu(group.Label, menu =>
            {
                foreach (var item in group.Items) menu.Add(ToItem(item));
            });
        }
    }

    public static void FillTray(CarbonTrayBuilder builder, IReadOnlyList<CarbonMenuItemSpec> items)
    {
        foreach (var item in items) builder.Add(ToTrayItem(item));
    }

    private static MenuItem ToItem(CarbonMenuItemSpec spec)
    {
        if (spec.Separator)
            return new MenuItem(null, null, null, string.Empty, IsSeparator: true);

        if (spec.Items is { } children)
            return new MenuItem(spec.Label ?? string.Empty, null, null, string.Empty, IsSeparator: false,
                Id: spec.Id, Children: children.Select(ToItem).ToArray());

        if (TryRole(spec.Role, out var role))
            return new MenuItem(spec.Label, null, null, string.Empty, IsSeparator: false, Role: role);

        // No C# handler exists for a declared item, so the click becomes an event. Bind() resolves
        // EventName into an emitter, which is the same path AddEventItem uses.
        return new MenuItem(
            spec.Label ?? string.Empty, OnClick: null, EventName: MenuItemEvent, Shortcut: spec.Shortcut,
            IsSeparator: false, Id: spec.Id,
            IsCheckItem: spec.Checked is not null, IsChecked: spec.Checked ?? false);
    }

    private static TrayItem ToTrayItem(CarbonMenuItemSpec spec)
    {
        if (spec.Separator) return new TrayItem(null, null, null, IsSeparator: true);

        if (spec.Items is { } children)
            return new TrayItem(spec.Label ?? string.Empty, null, null, IsSeparator: false,
                Children: children.Select(ToTrayItem).ToArray());

        return new TrayItem(
            spec.Label ?? string.Empty, OnClick: null, EventName: TrayItemEvent, IsSeparator: false);
    }

    /// <summary>Roles are matched case-insensitively; an unknown one is treated as a normal item.</summary>
    private static bool TryRole(string? role, out CarbonMenuRole parsed)
    {
        parsed = default;
        return !string.IsNullOrEmpty(role) && Enum.TryParse(role, ignoreCase: true, out parsed);
    }
}
