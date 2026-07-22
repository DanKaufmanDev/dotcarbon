using DotCarbon.Core.Bridge;
using DotCarbon.Core.Plugins;
using DotCarbon.Core.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace DotCarbon.Plugins.Battery;

/// <summary>
/// Reference plugin for the mobile native-binding pattern. The command surface lives here (net10.0,
/// shipped in the solution); the platform-native reads live in <c>DotCarbon.Plugins.Battery.Native</c>
/// (net10.0-android / net10.0-ios). A mobile app calls <c>app.UseBattery()</c> to register the native
/// <see cref="IBatteryProvider"/>; without it the plugin falls back to <see cref="DesktopBatteryProvider"/>.
/// </summary>
[CarbonPlugin("Battery", description: "Read device battery level and charging state.")]
[CarbonPluginPlatform("desktop", "android", "ios")]
[CarbonPermission("battery:default", "Allow reading battery status.", Commands = new[] { "battery:*" })]
public partial class BatteryPlugin : IPlugin
{
    private readonly IBatteryProvider _provider;

    public BatteryPlugin(AppHandle app)
        : this(app.Services.GetService<IBatteryProvider>() ?? new DesktopBatteryProvider()) { }

    // Injection seam for tests and for the native binding.
    internal BatteryPlugin(IBatteryProvider provider) => _provider = provider;

    public string Namespace => "battery";

    [CarbonCommand("status")]
    public BatteryStatus Status() => _provider.Read();
}
