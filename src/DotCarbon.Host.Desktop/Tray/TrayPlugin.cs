using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;

namespace DotCarbon.Host.Desktop;

/// <summary>
/// Exposes the live tray to the frontend (Task 2.10), so a JS app can update the icon it declared in
/// C# without round-tripping through a custom command of its own.
///
/// Registered by <c>UseTray</c> rather than by the user, because it is only meaningful once a tray
/// exists. The commands are the same surface as <see cref="CarbonTrayHandle"/> and inherit its
/// behaviour: each marshals to the platform's UI thread, unknown state is ignored rather than
/// throwing, and the platform differences (macOS-only title, no tooltip on Linux/SNI) apply here too.
/// </summary>
[CarbonPlugin("Tray", description: "Update the system tray icon from the frontend.")]
[CarbonPluginPlatform("desktop")]
[CarbonPermission("tray:default", "Allow all tray commands.", Commands = new[] { "tray:*" })]
public partial class TrayPlugin : IPlugin
{
    private readonly CarbonTrayHandle _tray = new();

    public string Namespace => "tray";

    [CarbonCommand("set_icon")]
    public void SetIcon(SetTrayIconArgs args) => _tray.SetIcon(args.Path, args.IsTemplate ?? false);

    [CarbonCommand("set_title")]
    public void SetTitle(SetTrayTitleArgs args) => _tray.SetTitle(args.Title);

    [CarbonCommand("set_tooltip")]
    public void SetTooltip(SetTrayTooltipArgs args) => _tray.SetTooltip(args.Tooltip);

    [CarbonCommand("set_visible")]
    public void SetVisible(SetTrayVisibleArgs args) => _tray.SetVisible(args.Visible);

    [CarbonCommand("remove")]
    public void Remove() => _tray.Remove();
}

/// <param name="IsTemplate">macOS only; see <see cref="CarbonTrayBuilder.SetIcon"/>.</param>
public sealed record SetTrayIconArgs(string Path, bool? IsTemplate);
public sealed record SetTrayTitleArgs(string Title);
public sealed record SetTrayTooltipArgs(string Tooltip);
public sealed record SetTrayVisibleArgs(bool Visible);
