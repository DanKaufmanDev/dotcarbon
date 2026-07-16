using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Host.Desktop;

/// <summary>
/// Exposes the live app menu to the frontend (Task 2.10). Items are addressed by the <c>id</c> given
/// when the menu was built in C#; unknown ids are ignored, matching <see cref="CarbonMenuHandle"/>.
///
/// Registered by <c>UseMenu</c> rather than by the user, since it is only meaningful once a menu
/// exists. <c>set_app_menu</c> goes further and replaces the menu with one the frontend describes.
/// </summary>
[CarbonPlugin("Menu", description: "Update the native app menu from the frontend.")]
[CarbonPluginPlatform("desktop")]
[CarbonPermission("menu:default", "Allow all menu commands.", Commands = new[] { "menu:*" })]
public partial class MenuPlugin : IPlugin
{
    private readonly CarbonMenuHandle _menu = new();

    public string Namespace => "menu";

    [CarbonCommand("set_enabled")]
    public void SetEnabled(SetMenuEnabledArgs args) => _menu.SetEnabled(args.Id, args.Enabled);

    [CarbonCommand("set_checked")]
    public void SetChecked(SetMenuCheckedArgs args) => _menu.SetChecked(args.Id, args.Checked);

    [CarbonCommand("set_label")]
    public void SetLabel(SetMenuLabelArgs args) => _menu.SetLabel(args.Id, args.Label);

    /// <summary>
    /// Replace the whole menu with one the frontend describes (Task 2.11). Clicks on the items come
    /// back as <c>menu:item_clicked</c> carrying the item's id, since a declared item has no C#
    /// handler to run.
    /// </summary>
    [CarbonCommand("set_app_menu")]
    public void SetAppMenu(SetAppMenuArgs args) =>
        _menu.SetMenu(menu => MenuSpec.Fill(menu, args.Menus));
}

public sealed record SetAppMenuArgs(IReadOnlyList<CarbonMenuGroupSpec> Menus);
public sealed record SetMenuEnabledArgs(string Id, bool Enabled);
public sealed record SetMenuCheckedArgs(string Id, bool Checked);
public sealed record SetMenuLabelArgs(string Id, string Label);
