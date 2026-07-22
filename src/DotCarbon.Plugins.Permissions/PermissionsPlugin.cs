using DotCarbon.Core.Bridge;
using DotCarbon.Core.Host;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;

namespace DotCarbon.Plugins.Permissions;

/// <summary>
/// Checks and prompts for the runtime permissions mobile platforms gate (camera, location,
/// notifications, …). The prompt itself belongs to the host (see <see cref="ICarbonPermissions"/>).
/// Desktop hosts gate none of these, so every permission reports as granted there and shared frontend
/// code can request unconditionally.
/// </summary>
[CarbonPlugin("Permissions", description: "Check and request runtime device permissions.")]
[CarbonPluginPlatform("desktop", "android", "ios")]
[CarbonPermission("permissions:default", "Allow checking and requesting device permissions.", Commands = new[] { "permissions:*" })]
public partial class PermissionsPlugin : IPlugin
{
    private readonly AppHandle? _app;
    private readonly ICarbonPermissions? _permissions;

    public PermissionsPlugin(AppHandle app) => _app = app;

    // Injection seam for tests.
    internal PermissionsPlugin(ICarbonPermissions permissions) => _permissions = permissions;

    public string Namespace => "permissions";

    private ICarbonPermissions? Backend => _permissions ?? _app?.PlatformPermissions;

    [CarbonCommand("status")]
    public Task<string> Status(PermissionArgs args) =>
        Backend?.StatusAsync(args.Permission) ?? Task.FromResult(CarbonPermissionState.Granted);

    [CarbonCommand("request")]
    public Task<string> Request(PermissionArgs args) =>
        Backend?.RequestAsync(args.Permission) ?? Task.FromResult(CarbonPermissionState.Granted);
}
